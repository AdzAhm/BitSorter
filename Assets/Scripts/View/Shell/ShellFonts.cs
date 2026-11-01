using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The shell's typeface, IBM Plex Sans as the rest of the game sets it (Part 11a). UI Toolkit draws text with
    /// TextCore's own font assets rather than TextMesh Pro's, so the shell makes one of its own from the two TTFs this
    /// component holds -- put in the scene by the scene builder, so the fonts ship in every build -- the first time a
    /// label asks.
    /// </summary>
    public sealed class ShellFonts : MonoBehaviour
    {
        [SerializeField] private Font _regular;
        [SerializeField] private Font _bold;

        /// <summary>IBM Plex Mono, the code's face (Part 12f): held here so it ships in every build, for <see cref="CodeFont"/>.</summary>
        [SerializeField] private Font _mono;

        private static ShellFonts _current;
        private static FontAsset _regularAsset, _boldAsset;

        private void OnEnable() => _current = this;

        private void OnDisable()
        {
            if (_current == this)
                _current = null;
        }

        /// <summary>The regular face; the panel's default face in a scene without this component.</summary>
        public static StyleFontDefinition Regular => Definition(false);

        /// <summary>The bold face, for a title.</summary>
        public static StyleFontDefinition Bold => Definition(true);

        /// <summary>The code's monospaced face, IBM Plex Mono; null in a scene without this component.</summary>
        public static Font Mono => _current == null ? null : _current._mono;

        /// <summary>The asset a label is drawn with, for a test asking whether every character it draws is in it; null with none.</summary>
        public static FontAsset AssetOf(bool bold)
        {
            Definition(bold);
            return bold ? _boldAsset : _regularAsset;
        }

        private static StyleFontDefinition Definition(bool bold)
        {
            Font font = _current == null ? null : bold ? _current._bold : _current._regular;
            if (font == null)
                return new StyleFontDefinition(StyleKeyword.Null);

            ref FontAsset asset = ref bold ? ref _boldAsset : ref _regularAsset;
            if (asset == null || asset.sourceFontFile != font)
                asset = Kept(font) ?? Made(font);
            return new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
        }

        /// <summary>Every character a face is given as it is made: printable ASCII, and what the interface's own words use beyond it.</summary>
        private static readonly string Printable = PrintableAscii() + "·→←↑↓©«»‹›×µμ…–—";

        /// <summary>
        /// The face made from <paramref name="font"/> earlier in this editor's session, or null: a domain reload loses the
        /// field that held it but not the asset, which <see cref="Made"/> keeps.
        /// </summary>
        /// <remarks>
        /// **A face is made once and keeps its glyphs, because FontEngine leaks when a face is given glyphs.** Unity 6000.3's
        /// FontEngine holds the kerning and ligature tables of one face at a time: giving glyphs to a face other than the last
        /// one given glyphs reads that face's tables again -- about 12 MB for Plex Sans -- and never frees the copy it drops.
        /// The shell draws in two faces, each was made empty on every entry to Play Mode, and text with new letters switched
        /// between them: a day of test runs grew the shared editor by about 20 GB under FontEngine (2026-10-29). Made full
        /// and kept, a face is given glyphs once an editor session, and drawing never gives it another.
        /// </remarks>
        private static FontAsset Kept(Font font)
        {
            foreach (FontAsset kept in Resources.FindObjectsOfTypeAll<FontAsset>())
                if (kept.sourceFontFile == font && kept.name == NameOf(font))
                    return kept;
            return null;
        }

        /// <summary>A face made from <paramref name="font"/>, every printable character in it, kept through unloads and reloads.</summary>
        private static FontAsset Made(Font font)
        {
            FontAsset made = FontAsset.CreateFontAsset(font);
            made.name = NameOf(font);
            made.TryAddCharacters(Printable, includeFontFeatures: true);
            made.hideFlags = HideFlags.HideAndDontSave;
            if (made.material != null)
                made.material.hideFlags = HideFlags.HideAndDontSave;
            if (made.atlasTextures != null)
                foreach (Texture2D atlas in made.atlasTextures)
                    if (atlas != null)
                        atlas.hideFlags = HideFlags.HideAndDontSave;
            return made;
        }

        private static string NameOf(Font font) => "Shell face " + font.name;

        private static string PrintableAscii()
        {
            var ascii = new char[127 - 32];
            for (int i = 0; i < ascii.Length; i++)
                ascii[i] = (char)(32 + i);
            return new string(ascii);
        }
    }
}
