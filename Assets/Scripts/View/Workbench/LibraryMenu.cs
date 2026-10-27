using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using EngineGate = BitSorter.Sim.GateKind;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The LIBRARY menu (Part 14d), under the TIMING tab's LIBRARY button: what the design is held to, in a line; no
    /// library, or the course's cells; and, with one, constants refused or allowed, a budget of gates, and each kind of
    /// gate taken out or put back. A cell's delays are typed on any gate of it, in its inspector, and every gate of the
    /// cell follows -- the table is the student's to fill in from the course's handout.
    /// </summary>
    public sealed class LibraryMenu : WaveMenu
    {
        /// <summary>The menu's name, its line's, and its items': a kind's is this and the kind.</summary>
        public const string RootName = "Workbench library", SaysName = "Workbench library says", NoneItem = "Workbench library none",
            CourseItem = "Workbench library course", ConstantsItem = "Workbench library constants", BudgetItem = "Workbench library budget",
            KindItem = "Workbench library kind ";

        private const float ItemWidth = 520f;

        /// <summary>The kinds a library holds, in the order the menu lists them.</summary>
        private static readonly EngineGate[] Kinds =
            { EngineGate.Not, EngineGate.Buf, EngineGate.And, EngineGate.Or, EngineGate.Nand, EngineGate.Nor, EngineGate.Xor, EngineGate.Xnor };

        private Label _says;
        private ShellButton _none, _course, _constants, _budget;
        private readonly Dictionary<EngineGate, ShellButton> _kinds = new Dictionary<EngineGate, ShellButton>();
        private GateLibrary _library;
        private long _delay = 1;

        /// <summary>What its line says now; empty while it is not up.</summary>
        public string Says => IsOpen ? _says.text : "";

        /// <summary>
        /// The menu on <paramref name="shell"/>: a choice made calls <paramref name="set"/> with the library it makes (null
        /// for none), and GATE BUDGET calls <paramref name="askBudget"/>.
        /// </summary>
        public static LibraryMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<GateLibrary> set, Action askBudget)
        {
            LibraryMenu menu = Make<LibraryMenu>(shell, owner, RootName, palette, ItemWidth);
            menu._says = menu.Note(SaysName);
            menu._none = menu.Item(NoneItem, "NO LIBRARY: ANY GATE, AT ANY DELAY", () => set(null));
            // The cells afresh; the budget and the rule on constants are the student's, and kept (the 14d review).
            menu._course = menu.Item(CourseItem, "COURSE CELLS", () => set(GateLibrary.CourseCells(menu._delay) with
            {
                NoConstants = menu._library?.NoConstants ?? false,
                GateBudget = menu._library?.GateBudget ?? -1,
            }));
            menu._constants = menu.Item(ConstantsItem, "", () => set(menu._library with { NoConstants = !menu._library.NoConstants }), breakBefore: true);
            menu._budget = menu.Item(BudgetItem, "", askBudget);
            bool first = true;
            foreach (EngineGate kind in Kinds)
            {
                EngineGate each = kind;
                menu._kinds[kind] = menu.Item(KindItem + kind, "", () => set(menu._library.Toggled(each, menu._delay)), breakBefore: first);
                first = false;
            }
            return menu;
        }

        /// <summary>Puts the menu up at <paramref name="screenPoint"/>, saying what <paramref name="library"/> holds; a kind put back takes <paramref name="delay"/>.</summary>
        public void Open(Vector2 screenPoint, GateLibrary library, long delay)
        {
            _library = library;
            _delay = delay;
            _says.text = library == null
                ? "No library: the design may be built of any gate, at any delay. Choose the course's cells to hold it to them."
                : library.Name + ": " + library.Listed() + ". A delay typed on a gate is its cell's, and every gate of the cell follows.";

            bool held = library != null;
            Offer(_none, held);
            Offer(_constants, held);
            Offer(_budget, held);
            _course.text = library?.Name == GateLibrary.CourseCellsName ? "COURSE CELLS AGAIN, EACH AT THE DEFAULT DELAY" : "COURSE CELLS";
            if (held)
            {
                _constants.text = library.NoConstants ? "ALLOW CONSTANTS" : "REFUSE CONSTANTS: 0S AND 1S FROM THE INPUTS";
                _budget.text = library.GateBudget >= 0
                    ? "GATE BUDGET: " + library.GateBudget.ToString(CultureInfo.InvariantCulture)
                    : "GATE BUDGET: NONE";
            }
            foreach (KeyValuePair<EngineGate, ShellButton> pair in _kinds)
            {
                Offer(pair.Value, held);
                if (held)
                    pair.Value.text = library.Allows(pair.Key) ? "TAKE OUT " + Caption(pair.Key) : "PUT " + Caption(pair.Key) + " BACK";
            }
            Show(screenPoint);
        }

        private static string Caption(EngineGate kind) => kind == EngineGate.Not ? "INV" : kind.ToString().ToUpperInvariant();
    }
}
