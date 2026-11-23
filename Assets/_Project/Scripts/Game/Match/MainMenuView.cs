using System;
using BeachTennis.AI;
using BeachTennis.Simulation.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeachTennis.Game.Match
{
    /// <summary>
    /// Main menu and options in UI Toolkit (docs/01 §2.9). Built in code with inline styles so it needs no
    /// UXML/USS assets beyond the PanelSettings in Resources/UI. Options are persisted in
    /// <see cref="GameSettings"/> and applied immediately.
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        public event Action<int, bool> PlayRequested;   // players per team, official format
        public event Action TrainingRequested;
        /// <summary>Raised when an option that changes the scene look (time of day, quality) changes.</summary>
        public event Action VisualsChanged;

        private static readonly Color Panel = new Color(0.05f, 0.08f, 0.12f, 0.82f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.25f, 1f);

        private UIDocument _document;
        private VisualElement _main, _options;
        private Label _summary;
        private Button _difficulty, _aid, _quality, _fps, _category, _time;
        private static readonly string[] TimeNames = { "Dia", "Pôr do sol", "Noite" };

        private static readonly string[] QualityNames = { "Baixa", "Média", "Alta", "Ultra" };

        private void Awake()
        {
            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = Resources.Load<PanelSettings>("UI/PanelSettings");
            Build(_document.rootVisualElement);
            ShowMain();
        }

        public void Show()
        {
            _document.rootVisualElement.style.display = DisplayStyle.Flex;
            ShowMain();
        }

        public void Hide() => _document.rootVisualElement.style.display = DisplayStyle.None;

        // ------------------------------------------------------------------ layout

        private void Build(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);

            var title = new Label("BEACH TENNIS 3D");
            title.style.fontSize = 76;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.marginBottom = 24;
            root.Add(title);

            _main = Card();
            _main.Add(MenuButton("Simples · Rápida (1 set curto)", () => PlayRequested?.Invoke(1, false), primary: true));
            _main.Add(MenuButton("Duplas · Rápida (1 set curto)", () => PlayRequested?.Invoke(2, false)));
            _main.Add(MenuButton("Simples · Oficial ITF (melhor de 3)", () => PlayRequested?.Invoke(1, true)));
            _main.Add(MenuButton("Treino", () => TrainingRequested?.Invoke()));
            _main.Add(MenuButton("Opções", ShowOptions));
            _summary = new Label();
            _summary.style.fontSize = 26;
            _summary.style.color = new Color(1f, 1f, 1f, 0.75f);
            _summary.style.marginTop = 10;
            _summary.style.unityTextAlign = TextAnchor.MiddleCenter;
            _main.Add(_summary);
            root.Add(_main);

            _options = Card();
            _difficulty = OptionRow("Dificuldade", () => { GameSettings.Difficulty = (Difficulty)(((int)GameSettings.Difficulty + 1) % 4); Refresh(); });
            _aid = OptionRow("Linha da trajetória", () => { GameSettings.TrajectoryAid = !GameSettings.TrajectoryAid; Refresh(); });
            _time = OptionRow("Horário", () => { GameSettings.TimeOfDay = (GameSettings.TimeOfDay + 1) % 3; Refresh(); VisualsChanged?.Invoke(); });
            _quality = OptionRow("Qualidade gráfica", () => { GameSettings.Quality = (CurrentQuality() + 1) % QualitySettings.count; Refresh(); VisualsChanged?.Invoke(); });
            _fps = OptionRow("Taxa de quadros", () => { GameSettings.HighFrameRate = !GameSettings.HighFrameRate; Refresh(); });
            _category = OptionRow("Categoria", () => { GameSettings.Category = GameSettings.Category == EventCategory.Men ? EventCategory.Women : EventCategory.Men; Refresh(); });
            _options.Add(MenuButton("Voltar", ShowMain, primary: true));
            root.Add(_options);
            Refresh();
        }

        private static VisualElement Card()
        {
            var card = new VisualElement();
            card.style.backgroundColor = Panel;
            card.style.paddingLeft = card.style.paddingRight = 36;
            card.style.paddingTop = card.style.paddingBottom = 28;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = 24;
            card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 24;
            card.style.alignItems = Align.Stretch;
            card.style.width = 980;
            return card;
        }

        private static Button MenuButton(string text, Action onClick, bool primary = false)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = 96;
            b.style.fontSize = 38;
            b.style.marginTop = b.style.marginBottom = 8;
            b.style.color = primary ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
            b.style.backgroundColor = primary ? Accent : ButtonColor;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = 16;
            b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 16;
            b.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
            return b;
        }

        /// <summary>A labelled row whose value button cycles the setting when tapped.</summary>
        private Button OptionRow(string label, Action onClick)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = row.style.marginBottom = 6;

            var name = new Label(label);
            name.style.fontSize = 34;
            name.style.color = Color.white;
            row.Add(name);

            Button value = MenuButton("", onClick);
            value.style.width = 520;
            row.Add(value);
            _options.Add(row);
            return value;
        }

        // ------------------------------------------------------------------ state

        private void ShowMain()
        {
            _main.style.display = DisplayStyle.Flex;
            _options.style.display = DisplayStyle.None;
            Refresh();
        }

        private void ShowOptions()
        {
            _main.style.display = DisplayStyle.None;
            _options.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private static int CurrentQuality() => GameSettings.Quality >= 0 ? GameSettings.Quality : QualitySettings.GetQualityLevel();

        private void Refresh()
        {
            if (_difficulty == null) return;
            _difficulty.text = AiProfile.For(GameSettings.Difficulty).Name;
            _aid.text = GameSettings.TrajectoryAid ? "Ligada" : "Desligada";
            _time.text = TimeNames[GameSettings.TimeOfDay];
            int q = CurrentQuality();
            _quality.text = q < QualityNames.Length ? QualityNames[q] : QualitySettings.names[q];
            _fps.text = GameSettings.HighFrameRate
                ? (FrameRateController.Supports120 ? "120 FPS" : "120 FPS (tela até " + FrameRateController.DisplayRefreshRate + " Hz)")
                : "60 FPS";
            _category.text = GameSettings.Category == EventCategory.Men ? "Masculina · 1,80 m" : "Feminina/Mista · 1,70 m";
            if (_summary != null)
                _summary.text = $"Dificuldade {AiProfile.For(GameSettings.Difficulty).Name} · " +
                                (GameSettings.Category == EventCategory.Men ? "masculina" : "feminina/mista") +
                                " · regras ITF 2026";
        }
    }
}
