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
                asset = FontAsset.CreateFontAsset(font);
            return new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
        }
    }
}
