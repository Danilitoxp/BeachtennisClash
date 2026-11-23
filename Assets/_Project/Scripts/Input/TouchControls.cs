using BeachTennis.Presentation;
using BeachTennis.Simulation.Players;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BeachTennis.Input
{
    /// <summary>
    /// Mobile controls: floating joystick on the left half, three shot buttons on the right.
    /// Hold a shot button to prepare, release to swing. Each release carries the touch's own event
    /// timestamp (not the frame time), so the game can compensate input latency when grading timing.
    ///
    /// Editor: the mouse simulates touches. Keyboard fallback: WASD/arrows move, J = main shot,
    /// K = lob, L = drop (frame-time precision only).
    /// </summary>
    [DefaultExecutionOrder(-3000)] // read touches before anything turns them into commands this frame
    public sealed class TouchControls : MonoBehaviour
    {
        private const float RefHeight = 1080f;
        private const float JoystickRadius = 120f;
        private const float KnobRadius = 55f;
        private const float DeadZone = 0.12f;

        private struct ButtonDef
        {
            public ShotType Type;
            public string Label;
            public Vector2 Center; // canvas units from the bottom-right safe corner
            public float Radius;
            public Image Image;
            public Text Text;
        }

        public Vector2 Move { get; private set; }
        public bool ShotHeld { get; private set; }
        public ShotType HeldType { get; private set; }
        public int ReleaseSerial { get; private set; }
        /// <summary>Time.realtimeSinceStartupAsDouble timeline.</summary>
        public double ReleaseRealtime { get; private set; }
        public ShotType ReleaseType { get; private set; }

        private ButtonDef[] _buttons;
        private int _buttonTouchId = -1;
        private int _heldButton = -1;
        private int _joyTouchId = -1;
        private Vector2 _joyOrigin;
        private bool _keyboardHeld;

        private Canvas _canvas;
        private RectTransform _joyBase;
        private RectTransform _joyKnob;
        private bool _serveMode;

        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color ButtonPressed = new Color(1f, 0.75f, 0.2f, 0.55f);
        private static readonly Color ButtonDisabled = new Color(1f, 1f, 1f, 0.07f);

        private void Awake()
        {
            BuildUi();
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
            TouchSimulation.Enable();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
            EnhancedTouchSupport.Disable();
        }

        /// <summary>Serve mode: the main button tosses (press) and hits (release); lob/drop are disabled.</summary>
        public void SetServeMode(bool serve)
        {
            _serveMode = serve;
            for (int i = 0; i < _buttons.Length; i++)
            {
                bool main = _buttons[i].Type == ShotType.Main;
                _buttons[i].Text.text = main ? (serve ? "SACAR" : "GOLPE") : _buttons[i].Label;
                _buttons[i].Image.color = !main && serve ? ButtonDisabled : ButtonIdle;
            }
        }

        private void Update()
        {
            float scale = Screen.height / RefHeight;
            Rect safe = Screen.safeArea;

            foreach (Touch t in Touch.activeTouches)
            {
                Vector2 pos = t.screenPosition;
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        int b = HitButton(pos, scale, safe);
                        if (b >= 0)
                        {
                            if (_heldButton < 0) Press(b, t.touchId);
                        }
                        else if (pos.x < Screen.width * 0.5f && _joyTouchId < 0)
                        {
                            _joyTouchId = t.touchId;
                            _joyOrigin = pos;
                            _joyBase.gameObject.SetActive(true);
                            _joyBase.anchoredPosition = pos / scale;
                            _joyKnob.anchoredPosition = Vector2.zero;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (t.touchId == _joyTouchId) UpdateJoystick(pos, scale);
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (t.touchId == _buttonTouchId) Release(t.time);
                        if (t.touchId == _joyTouchId)
                        {
                            _joyTouchId = -1;
                            Move = Vector2.zero;
                            _joyBase.gameObject.SetActive(false);
                        }
                        break;
                }
            }

            HandleKeyboard();
        }

        private void UpdateJoystick(Vector2 pos, float scale)
        {
            Vector2 delta = (pos - _joyOrigin) / scale;
            if (delta.magnitude > JoystickRadius) delta = delta.normalized * JoystickRadius;
            _joyKnob.anchoredPosition = delta;
            Vector2 v = delta / JoystickRadius;
            float m = v.magnitude;
            Move = m < DeadZone ? Vector2.zero : v * ((m - DeadZone) / (1f - DeadZone) / m);
        }

        private int HitButton(Vector2 screenPos, float scale, Rect safe)
        {
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_serveMode && _buttons[i].Type != ShotType.Main) continue;
                Vector2 center = new Vector2(safe.xMax, safe.yMin) + new Vector2(-_buttons[i].Center.x, _buttons[i].Center.y) * scale;
                // Generous hit area: 25% larger than the drawn circle.
                if ((screenPos - center).magnitude <= _buttons[i].Radius * 1.25f * scale) return i;
            }
            return -1;
        }

        private void Press(int button, int touchId)
        {
            _heldButton = button;
            _buttonTouchId = touchId;
            ShotHeld = true;
            HeldType = _buttons[button].Type;
            _buttons[button].Image.color = ButtonPressed;
        }

        private void Release(double eventTime)
        {
            if (_heldButton < 0) return;
            ReleaseType = _buttons[_heldButton].Type;
            ReleaseRealtime = eventTime;
            ReleaseSerial++;
            _buttons[_heldButton].Image.color = ButtonIdle;
            _heldButton = -1;
            _buttonTouchId = -1;
            ShotHeld = false;
        }

        private void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_joyTouchId < 0)
            {
                float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                var v = new Vector2(x, y);
                Move = v.sqrMagnitude > 1f ? v.normalized : v;
            }

            if (_heldButton < 0 || _keyboardHeld)
            {
                for (int i = 0; i < _buttons.Length; i++)
                {
                    var key = _buttons[i].Type == ShotType.Main ? kb.jKey : _buttons[i].Type == ShotType.Lob ? kb.kKey : kb.lKey;
                    if (key.wasPressedThisFrame && _heldButton < 0 && !(_serveMode && _buttons[i].Type != ShotType.Main))
                    {
                        Press(i, -2);
                        _keyboardHeld = true;
                    }
                    else if (key.wasReleasedThisFrame && _keyboardHeld && _heldButton == i)
                    {
                        Release(Time.realtimeSinceStartupAsDouble);
                        _keyboardHeld = false;
                    }
                }
            }
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("[TouchControls]");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, RefHeight);
            scaler.matchWidthOrHeight = 1f;

            // Joystick (positioned in screen-space canvas units from the bottom-left).
            _joyBase = Circle("JoystickBase", canvasGo.transform, JoystickRadius, new Color(1f, 1f, 1f, 0.15f), RuntimeSprites.SoftDisc);
            _joyBase.anchorMin = _joyBase.anchorMax = Vector2.zero;
            _joyKnob = Circle("JoystickKnob", _joyBase, KnobRadius, new Color(1f, 1f, 1f, 0.45f), RuntimeSprites.SoftDisc);
            _joyKnob.anchorMin = _joyKnob.anchorMax = new Vector2(0.5f, 0.5f);
            _joyBase.gameObject.SetActive(false);

            _buttons = new[]
            {
                new ButtonDef { Type = ShotType.Main, Label = "GOLPE", Center = new Vector2(240f, 240f), Radius = 110f },
                new ButtonDef { Type = ShotType.Lob, Label = "LOB", Center = new Vector2(500f, 140f), Radius = 78f },
                new ButtonDef { Type = ShotType.Drop, Label = "CURTA", Center = new Vector2(150f, 490f), Radius = 78f },
            };

            // Buttons hang off a bottom-right container sized to the safe area.
            var safeRoot = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeRoot.SetParent(canvasGo.transform, false);
            Rect sa = Screen.safeArea;
            safeRoot.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;

            for (int i = 0; i < _buttons.Length; i++)
            {
                var rt = Circle(_buttons[i].Label, safeRoot, _buttons[i].Radius, ButtonIdle, RuntimeSprites.SoftDisc);
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.anchoredPosition = new Vector2(-_buttons[i].Center.x, _buttons[i].Center.y);
                _buttons[i].Image = rt.GetComponent<Image>();

                var ring = Circle("Ring", rt, _buttons[i].Radius, new Color(1f, 1f, 1f, 0.6f), RuntimeSprites.Ring);
                ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 0.5f);

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var trt = textGo.GetComponent<RectTransform>();
                trt.SetParent(rt, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = trt.offsetMax = Vector2.zero;
                var text = textGo.GetComponent<Text>();
                text.fontSize = _buttons[i].Type == ShotType.Main ? 34 : 28;
                text.font = UiFonts.Get(text.fontSize);
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.raycastTarget = false;
                text.text = _buttons[i].Label;
                _buttons[i].Text = text;
            }
        }

        private static RectTransform Circle(string name, Transform parent, float radius, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }
    }
}
