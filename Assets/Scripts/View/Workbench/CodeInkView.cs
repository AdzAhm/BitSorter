using System;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Code written in its colours (5.1, Ahmad: "the system verilog doesn't have the default colorings ... all the text is
    /// white"): on a TextMesh Pro text -- the desktop's code editor, the CODE tab's listing -- each letter takes the colour of
    /// what it is (<see cref="CodeColouring"/>, <see cref="SchematicPalette.CodeInk"/>), as the browser's editor writes it.
    /// </summary>
    /// <remarks>
    /// **Its vertices are coloured, never its text marked up.** Rich text stays off, so every caret, selection and column is
    /// the text's own, letter for letter, as the input field counts them: the colours are written into the mesh in
    /// <see cref="TMP_Text.OnPreRenderText"/>, after TextMesh Pro lays the letters out and before it hands the mesh over.
    /// The text is read again only when it changes -- the same string, the same kinds -- so a still text costs nothing and
    /// makes nothing. Bold and italic would need marked-up text, so a comment is grey here where the browser's is italic too.
    /// </remarks>
    public sealed class CodeInkView : MonoBehaviour
    {
        private TMP_Text _text;
        private Color32[] _inks;
        private CodeKind[] _kinds = Array.Empty<CodeKind>();
        private string _read;

        /// <summary>Colours <paramref name="text"/> as code from now on, in <paramref name="palette"/>'s inks.</summary>
        public static CodeInkView On(TMP_Text text, SchematicPalette palette)
        {
            var view = text.gameObject.AddComponent<CodeInkView>();
            view._text = text;
            view._inks = new Color32[Enum.GetValues(typeof(CodeKind)).Length];
            for (int kind = 0; kind < view._inks.Length; kind++)
                view._inks[kind] = palette.CodeInk((CodeKind)kind);
            text.OnPreRenderText += view.Colour;
            return view;
        }

        /// <summary>The colour the letter at <paramref name="index"/> of the text is written in, for the tests.</summary>
        public Color32 InkAt(int index)
        {
            Read(_text.text ?? "");
            return index >= 0 && index < (_text.text ?? "").Length ? _inks[(int)_kinds[index]] : _inks[(int)CodeKind.Plain];
        }

        private void OnDestroy()
        {
            if (_text != null)
                _text.OnPreRenderText -= Colour;
        }

        /// <summary>The kinds of <paramref name="text"/>'s letters, worked out again only when it is another text.</summary>
        private void Read(string text)
        {
            if (ReferenceEquals(text, _read))
                return;
            if (_kinds.Length < text.Length)
                _kinds = new CodeKind[Mathf.NextPowerOfTwo(Math.Max(16, text.Length))];
            CodeColouring.Classify(text, _kinds);
            _read = text;
        }

        private void Colour(TMP_TextInfo info)
        {
            string text = _text.text ?? "";
            Read(text);
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo letter = info.characterInfo[i];
                if (!letter.isVisible)
                    continue;
                int source = letter.index;
                Color32 ink = source >= 0 && source < text.Length ? _inks[(int)_kinds[source]] : _inks[(int)CodeKind.Plain];
                Color32[] colours = info.meshInfo[letter.materialReferenceIndex].colors32;
                int v = letter.vertexIndex;
                ink.a = colours[v].a;   // the text's own alpha kept: a panel fading in fades its code too
                colours[v] = ink;
                colours[v + 1] = ink;
                colours[v + 2] = ink;
                colours[v + 3] = ink;
            }
        }
    }
}
