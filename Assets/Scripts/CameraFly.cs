// cameria is locked to the center of the scene
//      mouse is detached and can interact with the ui and is used to rotate the camera around the center
//      camera rotation is clamped to prevent flipping
// has option to detach and fly around
//     when detached, the camera can move freely in all directions
//      when detached, the mouse is locked and cannot interact with the ui
//      mouse can be detached and camera will stay in the free mode but cant move untill mouse is reatached or mode is switched back to orbit

// have canvas elements for
//      toggle camera mode
//      text to show current mode

//      have some text that shows in free cam to say to enable editing press ctrl to allow mouse to interact with ui then ctrl again to lock the mouse back to the camera

using UnityEngine;
using UnityEngine.UI;

public class CameraController : MonoBehaviour
{
    public enum CameraMode { Orbit, FreeFly }
    [Header("Camera Mode")]
    public CameraMode currentMode = CameraMode.Orbit;

    [Header("Orbit Settings")]
    public Transform orbitCenter;
    public float orbitDistance = 5f;
    public float orbitSpeed = 2f;
    public float minPitch = -80f;
    public float maxPitch = 80f;
    public float zoomSpeed = 5f;
    public float minZoomDistance = 1f;
    public float maxZoomDistance = 20f;

    [Header("Look Around Limits")]
    public float maxLookAroundYaw = 90f;
    public float maxLookAroundPitch = 60f;

    [Header("Free Fly Settings")]
    public float moveSpeed = 5.0f;
    public float turnSpeed = 60.0f;
    public float freeFlySensitivity = 2.0f;

    [Header("UI Elements")]
    public Text cameraModeText;
    public Button toggleModeButton;
    [Tooltip("Panel with orbit instructions")]
    public GameObject orbitInstructionsPanel;
    [Tooltip("Panel with free fly instructions")]
    public GameObject freeFlyInstructionsPanel;

    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private float lookAroundYaw = 0.0f;
    private float lookAroundPitch = 0.0f;
    private bool isCursorLocked = false;
    private Vector3 freeFlyPosition;
    private Quaternion freeFlyRotation;


    private void Start()
    {
        // Dont need to set the initial position and rotation here since we will set it in the orbit mode
        // freeFlyPosition = transform.position;
        // freeFlyRotation = transform.rotation;

        // Set up UI
        toggleModeButton.onClick.AddListener(ToggleCameraMode);
        UpdateUI();

        // Start in orbit mode
        SwitchToOrbitMode();
    }

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.R) && currentMode == CameraMode.Orbit)
        {
            lookAroundYaw = 0.0f;
            lookAroundPitch = 0.0f;
        }

        if (currentMode == CameraMode.Orbit)
            UpdateOrbitMode();
        else
            UpdateFreeFlyMode();

        UpdateUI();
    }

    private void ToggleCameraMode() =>
    currentMode = (currentMode == CameraMode.Orbit) ? CameraMode.FreeFly : CameraMode.Orbit;



    private void UpdateOrbitMode()
    {   // todo: change these controlls to remove the rotate, maybe use middle mouse to rotate around the center
        orbitDistance = Mathf.Clamp(orbitDistance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed, minZoomDistance, maxZoomDistance);

        if (Input.GetMouseButton(1))
        {   // Right mouse button - Orbit around center while facing it
            HandleCursorLock(true);

            yaw += orbitSpeed * Input.GetAxis("Mouse X");
            pitch -= orbitSpeed * Input.GetAxis("Mouse Y");
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
        else if (Input.GetMouseButton(2))
        {   // Middle mouse button - Free rotate camera
            HandleCursorLock(true);

            // Accumulate look-around rotation and clamp to reasonable limits
            lookAroundYaw += orbitSpeed * Input.GetAxis("Mouse X");
            lookAroundPitch -= orbitSpeed * Input.GetAxis("Mouse Y");
            lookAroundYaw = Mathf.Clamp(lookAroundYaw, -maxLookAroundYaw, maxLookAroundYaw);
            lookAroundPitch = Mathf.Clamp(lookAroundPitch, -maxLookAroundPitch, maxLookAroundPitch);
        }
        else // No mouse buttons pressed - unlock cursor
            HandleCursorLock(false);


        void HandleCursorLock(bool shouldLock)
        {   // handle cursor locking
            if (shouldLock && !isCursorLocked)
            {
                isCursorLocked = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (!shouldLock && isCursorLocked)
            {
                isCursorLocked = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }


        if (Input.GetMouseButton(1))
        {   // Right click - orbit around center

            // Calculate position based on yaw and pitch around the orbit center
            transform.position = orbitCenter.position + (/* orbit rotation */ Quaternion.Euler(pitch, yaw, 0f)) * Vector3.back * orbitDistance;

            // Apply look-around rotation on top of looking at center
            transform.rotation = (/*lookAtRotation*/     Quaternion.LookRotation(orbitCenter.position - transform.position)) *
                                 (/*lookAroundRotation*/ Quaternion.Euler(lookAroundPitch, lookAroundYaw, 0f));
        }
        else if (Input.GetMouseButton(2))
        {   // Middle click - free rotate while maintaining orbit distance

            // Calculate position based on current orbit
            transform.position = orbitCenter.position + (/* orbit rotation */ Quaternion.Euler(pitch, yaw, 0f)) * Vector3.back * orbitDistance;

            // Apply look-around rotation on top of looking at center
            transform.rotation = (/*lookAtRotation*/     Quaternion.LookRotation(orbitCenter.position - transform.position)) *
                                 (/*lookAroundRotation*/ Quaternion.Euler(lookAroundPitch, lookAroundYaw, 0f));
        }
        else
        {   // No mouse buttons - maintain orbit position and look at center with look-around

            // Maintain current orbit position based on yaw and pitch
            transform.position = orbitCenter.position + (/* orbit rotation */ Quaternion.Euler(pitch, yaw, 0f)) * Vector3.back * orbitDistance;

            // Apply look-around rotation on top of looking at center
            transform.rotation = (/*lookAtRotation*/     Quaternion.LookRotation(orbitCenter.position - transform.position)) *
                                 (/*lookAroundRotation*/ Quaternion.Euler(lookAroundPitch, lookAroundYaw, 0f));
        }
    }

    private void UpdateFreeFlyMode()
    {
        // Toggle cursor lock with Control key
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
        {
            isCursorLocked = !isCursorLocked;
            Cursor.lockState = isCursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isCursorLocked;
        }

        // Camera rotation only when cursor is locked
        if (isCursorLocked)
        {
            // Update yaw and pitch from mouse input
            yaw += freeFlySensitivity * Input.GetAxis("Mouse X");
            pitch -= freeFlySensitivity * Input.GetAxis("Mouse Y");
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            // Apply rotation to camera
            transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);

            // Horizontal movement based on camera rotation
            float x = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
            float z = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
            transform.Translate(x, 0, z);

            // Vertical movement in world space (absolute up/down)
            if (Input.GetKey(KeyCode.Space))
                transform.position += Vector3.up * moveSpeed * Time.deltaTime;

            else if (Input.GetKey(KeyCode.LeftShift))
                transform.position += Vector3.down * moveSpeed * Time.deltaTime;
        }
    }


    private void SwitchToOrbitMode()
    {
        currentMode = CameraMode.Orbit;
        isCursorLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Save free fly position for when we return
        freeFlyPosition = transform.position;
        freeFlyRotation = transform.rotation;

        // Reset to look at orbit center
        Vector3 directionToCenter = orbitCenter.position - transform.position;
        yaw = Mathf.Atan2(directionToCenter.x, directionToCenter.z) * Mathf.Rad2Deg;
        pitch = -Mathf.Asin(directionToCenter.y / directionToCenter.magnitude) * Mathf.Rad2Deg;

        UpdateUI();
    }

    private void UpdateUI()
    {
        // Defensive: Only update UI if all elements are assigned
        if (cameraModeText == null || toggleModeButton == null || orbitInstructionsPanel == null || freeFlyInstructionsPanel == null)
            return;
        cameraModeText.text = "Camera Mode: " + currentMode.ToString();
        orbitInstructionsPanel.SetActive(currentMode == CameraMode.Orbit);
        freeFlyInstructionsPanel.SetActive(currentMode == CameraMode.FreeFly);
        var btnText = toggleModeButton.GetComponentInChildren<Text>();
        if (btnText != null)
            btnText.text = "Switch to " + (currentMode == CameraMode.Orbit ? "Free Fly" : "Orbit");
    }

    // Debug: editor only to update ui like how it would be in play
    private void OnValidate() =>
        UpdateUI();
        
}