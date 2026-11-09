using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The panels folded away (Part 16d; Ahmad, 2026-11-08: "so we can have a larger view of the map"): PARTS, the side
    /// panel and the dock each fold by the arrow on the edge that faces the circuit, H folds all three while any is open and
    /// opens them once all are, and the folds are kept between launches (<see cref="FoldsKey"/>). Where each panel and its
    /// arrow is, folded or not, is <see cref="ShellLayout"/>'s rule; this builds the arrows and keeps the folds.
    /// </summary>
    /// <remarks>
    /// **A folded panel's inside is out of the way, not gone**: its shell element is not drawn, and the drawings it frames --
    /// the waves, the legend and the CODE tab's listing under the dock, the minimap at the side panel's foot -- are switched
    /// off with it and back on as it opens, every one as it was. The waves keep their width, so a fold never builds the stage
    /// again; the circuit's window is made another size, and a circuit fitted to it fitted again, as a drag on the splitter
    /// does. **Nothing the student needs folds away unasked**: the toolbar and its controls -- an exercise's GOAL and CHECK --
    /// never fold, the guided tutorial opens every panel as it starts (its first step lights the NOT in PARTS), and a CHECK
    /// that fails or a comparison with ModelSim, which say why in the TRANSCRIPT, open the dock to it.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The setting the folds are kept under, one number (<see cref="ShellFolds.Bits"/>); a machine that never folded one reads none.</summary>
        public const string FoldsKey = "workbench.folds";

        /// <summary>The fold arrows' names, which the tests look for.</summary>
        public const string PartsFoldName = "Workbench fold parts", SideFoldName = "Workbench fold side", DockFoldName = "Workbench fold dock";

        private ShellFolds _folds, _laidFolds;
        private ShellButton _partsFold, _sideFold, _dockFold;
        private Chevron _partsChevron, _sideChevron, _dockChevron;

        /// <summary>Which panels are folded now.</summary>
        public ShellFolds Folds => _folds;

        /// <summary>The three arrows, built after the panels so they stand over their edges, and under the pages built after them.</summary>
        private void BuildFolds(SchematicPalette palette)
        {
            _folds = ShellFolds.FromBits(Preferences.GetInt(FoldsKey, -1));
            _partsFold = PanelArrow(PartsFoldName, ShellFold.Parts, palette, out _partsChevron);
            _sideFold = PanelArrow(SideFoldName, ShellFold.Side, palette, out _sideChevron);
            _dockFold = PanelArrow(DockFoldName, ShellFold.Dock, palette, out _dockChevron);
        }

        private ShellButton PanelArrow(string name, ShellFold panel, SchematicPalette palette, out Chevron chevron)
        {
            ShellButton button = ShellTheme.Button(name, string.Empty, palette, () => Fold(panel));
            button.style.position = Position.Absolute;
            button.style.paddingLeft = button.style.paddingRight = button.style.paddingTop = button.style.paddingBottom = 0f;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0f;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius =
                button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 0.5f * ShellLayout.ArrowThickness;

            chevron = new Chevron { pickingMode = PickingMode.Ignore };
            chevron.style.position = Position.Absolute;
            chevron.style.left = chevron.style.top = chevron.style.right = chevron.style.bottom = 0f;
            button.Add(chevron);
            _shell.Root.Add(button);
            return button;
        }

        /// <summary>Folds <paramref name="panel"/> if it is open, and opens it if it is folded: its arrow's click.</summary>
        public void Fold(ShellFold panel) => SetFolds(_folds.Toggled(panel));

        /// <summary>H: every panel folded while any is open, and every one opened once all are folded.</summary>
        public void TogglePanels() => SetFolds(_folds.AllToggled());

        /// <summary>Every panel open, as the guided tutorial starts.</summary>
        public void OpenPanels() => SetFolds(default);

        /// <summary>The dock open, its other folds as they are: for what the TRANSCRIPT says that the student asked for.</summary>
        private void OpenDock()
        {
            if (_folds.Dock)
                SetFolds(_folds.Toggled(ShellFold.Dock));
        }

        private void SetFolds(ShellFolds folds)
        {
            if (folds == _folds)
                return;
            _folds = folds;
            Preferences.SetInt(FoldsKey, folds.Bits);
            Fit();
            if (_wavesClip != null)
                ShowDockTab(_dockTab);   // the dock's drawings off with it, or back as its tab has them
            PlaceMinimap();
        }

        /// <summary>The panels' shell elements drawn or not as they are folded, and each arrow where the regions put it, pointing the way it moves its panel.</summary>
        private void PlaceFolds()
        {
            if (_partsFold == null)
                return;
            Shown(_partsPanel, !_folds.Parts);
            Shown(_sidePanel, !_folds.Side);
            Shown(_dockPanel, !_folds.Dock);
            Shown(_splitter, !_folds.Dock);

            Color ink = SchematicPalette.Current.TextDim;
            Place(_partsFold, _regions.PartsFold);
            _partsChevron.Set(_folds.Parts ? Chevron.Way.Right : Chevron.Way.Left, ink);
            Place(_sideFold, _regions.SideFold);
            _sideChevron.Set(_folds.Side ? Chevron.Way.Left : Chevron.Way.Right, ink);
            Place(_dockFold, _regions.DockFold);
            _dockChevron.Set(_folds.Dock ? Chevron.Way.Up : Chevron.Way.Down, ink);
        }

        /// <summary>Whether <paramref name="pointer"/> is on a fold arrow, and whether that arrow's panel is folded.</summary>
        private bool OverFold(Vector2 pointer, out bool folded)
        {
            folded = false;
            if (_shell == null || _partsFold == null)
                return false;
            for (VisualElement e = _shell.PickAt(pointer); e != null; e = e.parent)
            {
                ShellFold? panel = e == _partsFold ? ShellFold.Parts : e == _sideFold ? ShellFold.Side : e == _dockFold ? ShellFold.Dock : (ShellFold?)null;
                if (panel.HasValue)
                {
                    folded = _folds[panel.Value];
                    return true;
                }
            }
            return false;
        }

        /// <summary>A fold arrow's chevron: two strokes meeting at a point, the way its click moves the panel.</summary>
        private sealed class Chevron : VisualElement
        {
            public enum Way { Left, Right, Up, Down }

            private Way _way;
            private Color _colour;
            private bool _set;

            public Chevron() => generateVisualContent += Draw;

            public void Set(Way way, Color colour)
            {
                if (_set && way == _way && colour == _colour)
                    return;
                _set = true;
                _way = way;
                _colour = colour;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                Vector2 c = contentRect.center;
                const float along = 3.2f, across = 2f;
                Vector2 tip, a, b;
                switch (_way)
                {
                    case Way.Left:
                        tip = new Vector2(c.x - across, c.y);
                        a = new Vector2(c.x + across, c.y - along);
                        b = new Vector2(c.x + across, c.y + along);
                        break;
                    case Way.Right:
                        tip = new Vector2(c.x + across, c.y);
                        a = new Vector2(c.x - across, c.y - along);
                        b = new Vector2(c.x - across, c.y + along);
                        break;
                    case Way.Up:
                        tip = new Vector2(c.x, c.y - across);
                        a = new Vector2(c.x - along, c.y + across);
                        b = new Vector2(c.x + along, c.y + across);
                        break;
                    default:
                        tip = new Vector2(c.x, c.y + across);
                        a = new Vector2(c.x - along, c.y - across);
                        b = new Vector2(c.x + along, c.y - across);
                        break;
                }

                Painter2D painter = context.painter2D;
                painter.strokeColor = _colour;
                painter.lineWidth = 1.6f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                painter.MoveTo(a);
                painter.LineTo(tip);
                painter.LineTo(b);
                painter.Stroke();
            }
        }
    }
}
