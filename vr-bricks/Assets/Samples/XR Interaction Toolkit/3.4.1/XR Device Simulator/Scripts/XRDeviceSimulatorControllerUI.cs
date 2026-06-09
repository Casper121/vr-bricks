using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace UnityEngine.XR.Interaction.Toolkit.Samples.DeviceSimulator
{
    [RequireComponent(typeof(XRDeviceSimulatorUI))]
    class XRDeviceSimulatorControllerUI : MonoBehaviour
    {
        [Header("General")]

        [SerializeField]
        Image m_ControllerImage;

        [SerializeField]
        Image m_ControllerOverlayImage;

        [Header("Primary Button")]

        [SerializeField]
        Image m_PrimaryButtonImage;

        [SerializeField]
        Text m_PrimaryButtonText;

        [SerializeField]
        Image m_PrimaryButtonIcon;

        [Header("Secondary Button")]

        [SerializeField]
        Image m_SecondaryButtonImage;

        [SerializeField]
        Text m_SecondaryButtonText;

        [SerializeField]
        Image m_SecondaryButtonIcon;

        [Header("Trigger")]

        [SerializeField]
        Image m_TriggerButtonImage;

        [SerializeField]
        Text m_TriggerButtonText;

        [SerializeField]
        Image m_TriggerButtonIcon;

        [Header("Grip")]

        [SerializeField]
        Image m_GripButtonImage;

        [SerializeField]
        Text m_GripButtonText;

        [SerializeField]
        Image m_GripButtonIcon;

        [Header("Thumbstick")]

        [SerializeField]
        Image m_ThumbstickButtonImage;

        [SerializeField]
        Text m_ThumbstickButtonText;

        [SerializeField]
        Image m_ThumbstickButtonIcon;

        [Header("Menu")]

        [SerializeField]
        Image m_MenuButtonImage;

        [SerializeField]
        Text m_MenuButtonText;

        [SerializeField]
        Image m_MenuButtonIcon;

        XRDeviceSimulatorUI m_MainUIManager;

        bool m_PrimaryButtonActivated;
        bool m_SecondaryButtonActivated;
        bool m_TriggerActivated;
        bool m_GripActivated;
        bool m_MenuActivated;
        bool m_XAxisTranslateActivated;
        bool m_YAxisTranslateActivated;

        protected void Awake()
        {
            m_MainUIManager = GetComponent<XRDeviceSimulatorUI>();
        }

        internal void Initialize(XRDeviceSimulator simulator)
        {
            SetTextSafe(m_PrimaryButtonText, GetControlDisplayName(simulator.primaryButtonAction, 0, "Primary"));
            SetTextSafe(m_SecondaryButtonText, GetControlDisplayName(simulator.secondaryButtonAction, 0, "Secondary"));
            SetTextSafe(m_GripButtonText, GetControlDisplayName(simulator.gripAction, 0, "Grip"));
            SetTextSafe(m_TriggerButtonText, GetControlDisplayName(simulator.triggerAction, 0, "Trigger"));
            SetTextSafe(m_MenuButtonText, GetControlDisplayName(simulator.menuAction, 0, "Menu"));

            var disabledImgColor = m_MainUIManager.disabledColor;

            if (m_ThumbstickButtonImage != null)
                m_ThumbstickButtonImage.color = disabledImgColor;

            if (m_ControllerImage != null)
                m_ControllerImage.color = m_MainUIManager.disabledDeviceColor;

            if (m_ControllerOverlayImage != null)
                m_ControllerOverlayImage.color = disabledImgColor;
        }

        internal void SetAsActiveController(bool active, XRDeviceSimulator simulator, bool isRestingHand = false)
        {
            InputAction action = null;

            if (isRestingHand && simulator.restingHandAxis2DAction != null)
                action = simulator.restingHandAxis2DAction.action;
            else if (simulator.axis2DAction != null)
                action = simulator.axis2DAction.action;

            SetTextSafe(m_ThumbstickButtonText, BuildControlsText(action, "Move"));

            UpdateButtonVisuals(active, m_PrimaryButtonIcon, m_PrimaryButtonText, GetControl(simulator.primaryButtonAction, 0));
            UpdateButtonVisuals(active, m_SecondaryButtonIcon, m_SecondaryButtonText, GetControl(simulator.secondaryButtonAction, 0));
            UpdateButtonVisuals(active, m_TriggerButtonIcon, m_TriggerButtonText, GetControl(simulator.triggerAction, 0));
            UpdateButtonVisuals(active, m_GripButtonIcon, m_GripButtonText, GetControl(simulator.gripAction, 0));
            UpdateButtonVisuals(active, m_MenuButtonIcon, m_MenuButtonText, GetControl(simulator.menuAction, 0));
            UpdateButtonVisuals(active || isRestingHand, m_ThumbstickButtonIcon, m_ThumbstickButtonText, GetControl(simulator.axis2DAction, 0));

            if (active)
            {
                UpdateButtonColor(m_PrimaryButtonImage, m_PrimaryButtonActivated);
                UpdateButtonColor(m_SecondaryButtonImage, m_SecondaryButtonActivated);
                UpdateButtonColor(m_TriggerButtonImage, m_TriggerActivated);
                UpdateButtonColor(m_GripButtonImage, m_GripActivated);
                UpdateButtonColor(m_MenuButtonImage, m_MenuActivated);
                UpdateButtonColor(m_ThumbstickButtonImage, m_XAxisTranslateActivated || m_YAxisTranslateActivated);

                if (m_ControllerImage != null)
                    m_ControllerImage.color = m_MainUIManager.deviceColor;

                if (m_ControllerOverlayImage != null)
                    m_ControllerOverlayImage.color = m_MainUIManager.enabledColor;
            }
            else
            {
                UpdateDisableControllerButton(m_PrimaryButtonActivated, m_PrimaryButtonImage, m_PrimaryButtonIcon, m_PrimaryButtonText);
                UpdateDisableControllerButton(m_SecondaryButtonActivated, m_SecondaryButtonImage, m_SecondaryButtonIcon, m_SecondaryButtonText);
                UpdateDisableControllerButton(m_TriggerActivated, m_TriggerButtonImage, m_TriggerButtonIcon, m_TriggerButtonText);
                UpdateDisableControllerButton(m_GripActivated, m_GripButtonImage, m_GripButtonIcon, m_GripButtonText);
                UpdateDisableControllerButton(m_MenuActivated, m_MenuButtonImage, m_MenuButtonIcon, m_MenuButtonText);

                if (!isRestingHand)
                    UpdateDisableControllerButton(m_XAxisTranslateActivated || m_YAxisTranslateActivated, m_ThumbstickButtonImage, m_ThumbstickButtonIcon, m_ThumbstickButtonText);
                else if (m_ThumbstickButtonImage != null)
                    m_ThumbstickButtonImage.color = m_MainUIManager.buttonColor;

                if (m_ControllerImage != null)
                    m_ControllerImage.color = m_MainUIManager.disabledDeviceColor;

                if (m_ControllerOverlayImage != null)
                    m_ControllerOverlayImage.color = m_MainUIManager.disabledColor;
            }
        }

        void UpdateDisableControllerButton(bool active, Image button, Image buttonIcon, Text buttonText)
        {
            if (button == null || buttonIcon == null || buttonText == null)
                return;

            if (active)
            {
                var tmpColor = m_MainUIManager.selectedColor;
                tmpColor.a = 0.5f;
                button.color = tmpColor;
                buttonText.gameObject.SetActive(true);
                buttonIcon.gameObject.SetActive(true);
            }
            else
            {
                button.color = m_MainUIManager.disabledButtonColor;
                buttonText.gameObject.SetActive(false);
                buttonIcon.gameObject.SetActive(false);
            }
        }

        void UpdateButtonVisuals(bool active, Image buttonIcon, Text buttonText, InputControl control)
        {
            if (buttonIcon == null || buttonText == null)
                return;

            buttonText.gameObject.SetActive(active);
            buttonIcon.gameObject.SetActive(active);

            var color = active ? m_MainUIManager.enabledColor : m_MainUIManager.disabledColor;
            buttonText.color = color;
            buttonIcon.color = color;

            buttonIcon.transform.localScale = Vector3.one;

            if (control == null)
            {
                buttonIcon.sprite = m_MainUIManager.keyboardSprite;
                return;
            }

            buttonIcon.sprite = m_MainUIManager.GetInputIcon(control);

            switch (control.name)
            {
                case "leftButton":
                    buttonText.text = "L Mouse";
                    buttonIcon.color = Color.white;
                    buttonIcon.transform.localScale = new Vector3(-1f, 1f, 1f);
                    break;

                case "rightButton":
                    buttonText.text = "R Mouse";
                    buttonIcon.color = Color.white;
                    break;

                default:
                    buttonIcon.sprite = m_MainUIManager.keyboardSprite;
                    break;
            }
        }

        void UpdateButtonColor(Image image, bool activated)
        {
            if (image == null)
                return;

            image.color = activated ? m_MainUIManager.selectedColor : m_MainUIManager.buttonColor;
        }

        internal void OnPrimaryButton(bool activated)
        {
            m_PrimaryButtonActivated = activated;
            UpdateButtonColor(m_PrimaryButtonImage, activated);
        }

        internal void OnSecondaryButton(bool activated)
        {
            m_SecondaryButtonActivated = activated;
            UpdateButtonColor(m_SecondaryButtonImage, activated);
        }

        internal void OnTrigger(bool activated)
        {
            m_TriggerActivated = activated;
            UpdateButtonColor(m_TriggerButtonImage, activated);
        }

        internal void OnGrip(bool activated)
        {
            m_GripActivated = activated;
            UpdateButtonColor(m_GripButtonImage, activated);
        }

        internal void OnMenu(bool activated)
        {
            m_MenuActivated = activated;
            UpdateButtonColor(m_MenuButtonImage, activated);
        }

        internal void OnXAxisTranslatePerformed(bool activated)
        {
            m_XAxisTranslateActivated = activated;
            UpdateButtonColor(m_ThumbstickButtonImage, activated || m_YAxisTranslateActivated);
        }

        internal void OnZAxisTranslatePerformed(bool activated)
        {
            m_YAxisTranslateActivated = activated;
            UpdateButtonColor(m_ThumbstickButtonImage, activated || m_XAxisTranslateActivated);
        }

        static void SetTextSafe(Text text, string value)
        {
            if (text != null)
                text.text = value;
        }

        static InputControl GetControl(InputActionReference reference, int index)
        {
            if (reference == null || reference.action == null)
                return null;

            var controls = reference.action.controls;

            if (index < 0 || index >= controls.Count)
                return null;

            return controls[index];
        }

        static string GetControlDisplayName(InputActionReference reference, int index, string fallback)
        {
            InputControl control = GetControl(reference, index);

            if (control == null)
                return fallback;

            return string.IsNullOrEmpty(control.displayName) ? fallback : control.displayName;
        }

        static string BuildControlsText(InputAction action, string fallback)
        {
            if (action == null || action.controls.Count == 0)
                return fallback;

            string text = "";

            for (int i = 0; i < action.controls.Count; i++)
            {
                if (i > 0)
                    text += ", ";

                string displayName = action.controls[i].displayName;
                text += string.IsNullOrEmpty(displayName) ? action.controls[i].name : displayName;
            }

            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}