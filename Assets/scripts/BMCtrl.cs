using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BMDSwitcherAPI;
using UnityEngine.UI;
using System.Xml.Serialization;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using UnityEditor;
//using UnityEngine.Assertions.Must;

public class BMCtrl : MonoBehaviour
{
    [SerializeField] string connectIp="192.168.10.240";

    private IUnknownConstantAttribute m_UnknownConstantAttribute;
    private IBMDSwitcherDiscovery m_switcherDiscovery;
    private IBMDSwitcher m_switcher;
    private IBMDSwitcherMixEffectBlock m_mixEffectBlock;

    private SwitcherPanelCSharp.MixEffectBlockMonitor m_mixEffectBlockMonitor;

    [SerializeField]
    private List<IBMDSwitcherInput> _inputList = new List<IBMDSwitcherInput>();


    [SerializeField]
    Slider slider;

    [SerializeField]
    Dropdown ProgramDropdown;

    [SerializeField]
    Dropdown PreviewDropdown;

    [SerializeField]
    Text DeviceName;

    [SerializeField]
    InputField AutoFrames;

    private bool sliderUp2Under = false;
    private bool bSliderEvent = false;
    private bool bnFramesRemainingEvent = false;


    // Start is called before the first frame update

    #region MonoBehaviour

    void Start()
    {
        SwitcherDisconnected();

        ConnectAtem(connectIp);

        //  スイッチ名取得
        {
            string switcherName;
            m_switcher.GetProductName(out switcherName);
            Debug.Log(switcherName);
            DeviceName.text = switcherName;
        }

        GetSwitcherInputIterator();


        GetSwitcherMixEffectBlock();


        if (m_mixEffectBlock == null)
        {
            Debug.LogError("Unexpected: Could not get first mix effect block");
            return;
        }


        UpdateProgramButtonSelection();
        UpdatePreviewButtonSelection();
        UpdateTransitionFramesRemaining();

        m_mixEffectBlockMonitor = new SwitcherPanelCSharp.MixEffectBlockMonitor();

        m_mixEffectBlockMonitor.ProgramInputChanged += new SwitcherPanelCSharp.SwitcherEventHandler((s, a) => UpdateProgramButtonSelection());
        m_mixEffectBlockMonitor.TransitionPositionChanged += new SwitcherPanelCSharp.SwitcherEventHandler((s, a) => SliderEvent());
        m_mixEffectBlockMonitor.TransitionFramesRemainingChanged += new SwitcherPanelCSharp.SwitcherEventHandler((s, a) => UpdateTransitionFramesRemaining());


        // Install MixEffectBlockMonitor callbacks:
        m_mixEffectBlock.AddCallback(m_mixEffectBlockMonitor);

    }

    // Update is called once per frame
    void Update()
    {
        TransitionPositionEvent();
        FramesRemainingEvent();
    }


    private void OnDestroy()
    {
        SwitcherDisconnected();
    }
    #endregion MonoBehaviour

    private void GetSwitcherMixEffectBlock()
    {
        // We want to get the first Mix Effect block (ME 1). We create a ME iterator,
        // and then get the first one:
        m_mixEffectBlock = null;

        IBMDSwitcherMixEffectBlockIterator meIterator = null;
        IntPtr meIteratorPtr;
        Guid meIteratorIID = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
        m_switcher.CreateIterator(ref meIteratorIID, out meIteratorPtr);
        if (meIteratorPtr != null)
        {
            meIterator = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(meIteratorPtr);
        }
        Marshal.Release(meIteratorPtr); // Release必須？

        if (meIterator != null)
        {
            meIterator.Next(out m_mixEffectBlock);
        }

    }
    private void GetSwitcherInputIterator()
    {
        IBMDSwitcherInputIterator inputIterator = null;
        IntPtr inputIteratorPtr;
        Guid inputIteratorIID = typeof(IBMDSwitcherInputIterator).GUID;
        m_switcher.CreateIterator(ref inputIteratorIID, out inputIteratorPtr);
        if (inputIteratorPtr != null)
        {
            inputIterator = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIteratorPtr);
        }
        Marshal.Release(inputIteratorPtr); // Release必須？

        if (inputIterator != null)
        {
            IBMDSwitcherInput input;
            inputIterator.Next(out input);
            while (input != null)
            {
                string name = "";
                long inputId;

                input.GetInputId(out inputId);

                if (input != null)
                {
                    input.GetLongName(out name);
                    ProgramDropdown.options.Add(new Dropdown.OptionData { text = name });
                    PreviewDropdown.options.Add(new Dropdown.OptionData { text = name });
                }

                _inputList.Add(input);
                inputIterator.Next(out input);
            }


        }
    }
    private void ConnectAtem(string connectIp)
    {

        m_switcherDiscovery = new CBMDSwitcherDiscovery();
        if (m_switcherDiscovery == null)
        {
            Debug.LogError("Could not create Switcher Discovery Instance.\nATEM Switcher Software may not be installed.");

        }

        _BMDSwitcherConnectToFailure failReason = 0;
        string address = connectIp;

        try
        {
            // Note that ConnectTo() can take several seconds to return, both for success or failure,
            // depending upon hostname resolution and network response times, so it may be best to
            // do this in a separate thread to prevent the main GUI thread blocking.
            m_switcherDiscovery.ConnectTo(address, out m_switcher, out failReason);
        }
        catch (COMException)
        {
            // An exception will be thrown if ConnectTo fails. For more information, see failReason.
            switch (failReason)
            {
                case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureNoResponse:
                    Debug.LogError("No response from Switcher");
                    break;
                case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureIncompatibleFirmware:
                    Debug.LogError("Switcher has incompatible firmware");
                    break;
                default:
                    Debug.LogError("Connection failed for unknown reason");
                    break;
            }
            return;
        }
    }

    public void SwitcherDisconnected()
    {
        if (m_mixEffectBlock != null)
        {

            // Remove callback
            m_mixEffectBlock.RemoveCallback(m_mixEffectBlockMonitor);

            // Release reference
            m_mixEffectBlock = null;

        }

        if(m_switcherDiscovery != null)
        {
            m_switcherDiscovery = null;
        }
    }

    public void CutButton()
    {
        m_mixEffectBlock.PerformCut();
        ProgramPreviewChanged();
    }

    public void AutoButton()
    {
        m_mixEffectBlock.PerformAutoTransition();
    }

    public void ProgramChanged()
    {
        long id = 0;
        _inputList[ProgramDropdown.value].GetInputId(out id);
        m_mixEffectBlock.SetProgramInput(id);
    }

    public void PreviewChanged()
    {
        long id = 0;
        _inputList[PreviewDropdown.value].GetInputId(out id);
        m_mixEffectBlock.SetPreviewInput(id);
    }

    private void ProgramPreviewChanged()
    {
        int PreviousId = ProgramDropdown.value;
        ProgramDropdown.value = PreviewDropdown.value;
        PreviewDropdown.value = PreviousId;
    }
    private void SliderEvent()
    {
        bSliderEvent = true;

    }

    private void TransitionPositionEvent()
    {
        if(bSliderEvent && !Input.GetMouseButton(0))
        {
            double position = 0;
            m_mixEffectBlock.GetTransitionPosition(out position);

            bool change = false;
            if(position == 0)
            {
                change = true;
                position = 1;
            }
            slider.value = (float)(sliderUp2Under ? 1 - position : position);

            if ( change)
            {
                sliderUp2Under = !sliderUp2Under;
                ProgramPreviewChanged();
            }


        }
        bSliderEvent = false;
    }
    public void SliderChangedEvent()
    {
        if (m_mixEffectBlock != null && Input.GetMouseButton(0))
        {
            double position = sliderUp2Under ? 1 - slider.value : slider.value;

            m_mixEffectBlock.SetTransitionPosition(position);

            if (slider.value==1.0f)
            {
                sliderUp2Under = true;
                ProgramPreviewChanged();
            }

            if (slider.value == 0.0f)
            {
                sliderUp2Under = false;
                ProgramPreviewChanged();
            }
            Debug.Log($"1 {position}");

        }
    }
    private void UpdateProgramButtonSelection()
    {
        long ProgramId;
        m_mixEffectBlock.GetProgramInput(out ProgramId);
        int index = GetSelectButton(ProgramId);
        ProgramDropdown.value = index;
    }

    private void UpdatePreviewButtonSelection()
    {
        long PreviewId;
        m_mixEffectBlock.GetPreviewInput(out PreviewId);
        int index = GetSelectButton(PreviewId);
        PreviewDropdown.value = index;
    }

    private int GetSelectButton(long buttonId )
    {
        int index = 0;
        long setId =0;

        foreach (var input in _inputList)
        {
            input.GetInputId(out setId);
            if (setId == buttonId )
            {
                break;
            }
            index++;
        }

        return index;
    }

    private void UpdateTransitionFramesRemaining()
    {
        bnFramesRemainingEvent = true;
    }

    private void FramesRemainingEvent()
    {
        if(bnFramesRemainingEvent)
        {
            uint framesRemaining;

            m_mixEffectBlock.GetTransitionFramesRemaining(out framesRemaining);
            AutoFrames.text = String.Format("{0}", framesRemaining);

        }

        bnFramesRemainingEvent = false;
    }

}
