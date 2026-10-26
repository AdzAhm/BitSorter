using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// RENAME on a row's menu (Part 9b-2b): a field holding the row's name, Enter giving the row or
    /// group the name typed -- an empty one gives a row its own back -- and a click off it, or Escape,
    /// leaving it as it was.
    /// </summary>
    /// <remarks>
    /// In the shell since Part 11b-3 (<see cref="ShellPopup"/>), over the docked panels, holding Escape as the menus do
    /// (<see cref="IHoldsEscape"/>): while the field has the keyboard it answers Enter and Escape itself, and letting it go
    /// by a click off it makes nothing (<see cref="ShellTheme.Field"/>'s <c>submitOnLeave</c>).
    /// </remarks>
    public sealed class WaveRenameField : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The object's name, which the tests look for.</summary>
        public const string RootName = "Workbench rename";

        /// <summary>The name of the same field asking for a breakpoint's value (Part 9b-3).</summary>
        public const string BreakValueName = "Workbench break value";

        /// <summary>The name of the same field asking for an input's value over a stretch (Part 9b-3).</summary>
        public const string SetValueName = "Workbench set value";

        /// <summary>The name a block made from a selection is asked for under (Part 12b).</summary>
        public const string BlockNameName = "Workbench block name";

        /// <summary>The field a new code block's name is asked in (Part 12d).</summary>
        public const string CodeNameName = "Workbench code block name";

        private const float Width = 320f, Pad = 12f, TitleHeight = 24f, FieldHeight = 38f;

        private ShellPopup _popup;
        private Label _title;
        private TextField _field;
        private Action<string> _onRename;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => _popup != null && _popup.IsOpen;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || (_popup != null && _popup.ClosedFrame == Time.frameCount);

        /// <summary>The field it asks in: what a test types into.</summary>
        public TextField Field => _field;

        /// <summary>
        /// The field on <paramref name="shell"/>, shut, naming through <paramref name="onRename"/> -- or, named and titled
        /// otherwise, asking for any one line of text, as BREAK ON VALUE does; its component on a child of <paramref name="owner"/>.
        /// </summary>
        public static WaveRenameField Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<string> onRename,
            string name = RootName, string titleText = "RENAME", string placeholder = "empty for its own name")
        {
            var host = new GameObject(name);
            host.transform.SetParent(owner, false);
            var rename = host.AddComponent<WaveRenameField>();
            rename._onRename = onRename;
            rename._popup = new ShellPopup(shell, name, palette);

            rename._title = ShellTheme.Label("title", titleText, UiType.Caption, palette.TextDim);
            ShellTheme.Place(rename._title, Pad, Pad, Width - 2f * Pad, TitleHeight);
            ShellTheme.Ellipsis(rename._title);
            rename._popup.Panel.Add(rename._title);

            rename._field = ShellTheme.Field(name + " field", placeholder, palette, text => rename.Confirm(text), submitOnLeave: false);
            ShellTheme.Place(rename._field, Pad, Pad + 30f, Width - 2f * Pad, FieldHeight);
            rename._field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                    rename.Close();
            });
            rename._popup.Panel.Add(rename._field);
            return rename;
        }

        /// <summary>
        /// Opens the field holding <paramref name="name"/>, its corner at <paramref name="screenPoint"/>, with
        /// the keyboard -- titled <paramref name="title"/> when it says what it is for each time, as SET does.
        /// </summary>
        public void Open(string name, Vector2 screenPoint, string title = null)
        {
            if (title != null)
                _title.text = title;
            _popup.Open(screenPoint, new Vector2(Width, Pad + 30f + FieldHeight + Pad));
            _field.SetValueWithoutNotify(name ?? "");
            _field.Focus();
            _field.SelectAll();
            UiEscape.Opened(this);
        }

        /// <summary>Gives the row <paramref name="name"/> and closes, as Enter does.</summary>
        public void Confirm(string name)
        {
            if (!IsOpen)
                return;
            Close();
            _onRename?.Invoke(name);
        }

        /// <summary>Shuts it, leaving the name as it was.</summary>
        public void Close() => _popup?.Close();

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            // While the field has the keyboard it answers Escape and Enter itself; let go of, the popup does.
            Keyboard keyboard = UiText.Keyboard;
            if (!IsOpen || keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                Confirm(_field.value);
        }
    }
}
