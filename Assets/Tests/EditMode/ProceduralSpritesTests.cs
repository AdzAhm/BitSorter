using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The sprite factory's cache, which hands out Unity objects and therefore has to cope with
    /// them being destroyed underneath it.
    /// </summary>
    /// <remarks>
    /// Every sprite here is generated at runtime and kept in a plain static dictionary. A generated
    /// texture is not an asset, so anything that sweeps untracked objects takes it while the
    /// dictionary entry survives -- and the next caller is handed a destroyed sprite, which an
    /// Image reads back as no sprite at all.
    ///
    /// Not hypothetical. A WebGL build finished seventeen seconds before an EditMode run, and two
    /// panel tests that had passed an hour earlier failed with a null sprite. It is invisible in a
    /// player, where nothing unloads mid-session, and it only ever showed up in the editor.
    /// </remarks>
    public class ProceduralSpritesTests
    {
        /// <summary>
        /// Unity objects compare equal to null once destroyed, which NUnit's own IsNotNull does not
        /// notice -- so every assertion here goes through the overloaded operator.
        /// </summary>
        private static void AssertAlive(Object thing, string what) =>
            Assert.IsTrue(thing != null, what);

        [Test]
        public void ASpriteDestroyedUnderTheCache_IsBuiltAgainRatherThanHandedBackDead()
        {
            Sprite first = ProceduralSprites.Panel();
            AssertAlive(first, "sanity: the factory produced a sprite");

            Object.DestroyImmediate(first.texture);
            Object.DestroyImmediate(first);

            Sprite again = ProceduralSprites.Panel();

            AssertAlive(again, "the cache handed back a destroyed sprite");
            AssertAlive(again.texture, "the cache handed back a sprite whose texture was destroyed");
        }

        /// <summary>
        /// The texture alone can go, leaving a sprite that is technically alive and draws nothing.
        /// </summary>
        [Test]
        public void ASpriteWhoseTextureWentMissing_IsAlsoBuiltAgain()
        {
            Sprite first = ProceduralSprites.RoundedSquare();
            AssertAlive(first, "sanity: the factory produced a sprite");

            Object.DestroyImmediate(first.texture);

            Sprite again = ProceduralSprites.RoundedSquare();

            AssertAlive(again, "the cache kept a sprite with no texture behind it");
            AssertAlive(again.texture, "the cache kept a sprite with no texture behind it");
        }

        /// <summary>
        /// While nothing has been destroyed, the cache is still a cache.
        /// </summary>
        /// <remarks>
        /// The pair to the two above: a check that rebuilt on every call would satisfy them and
        /// quietly make every panel regenerate its backdrop.
        /// </remarks>
        [Test]
        public void AskingTwiceForTheSameSprite_ReturnsTheSameOne()
        {
            Assert.AreSame(ProceduralSprites.Panel(), ProceduralSprites.Panel());
            Assert.AreSame(ProceduralSprites.Circle(), ProceduralSprites.Circle());

            // The board tile is keyed differently from the masks -- on the palette, because it bakes
            // its colours in -- and a lookup and a store that disagreed on that key would rebuild a
            // texture on every call without anything on screen looking wrong.
            Assert.AreSame(ProceduralSprites.BoardTile(), ProceduralSprites.BoardTile(),
                "the board tile is rebuilt on every call, and every rebuild is a texture nothing frees");
        }

        /// <summary>
        /// Two looks get two board tiles, because the tile's colours are baked into its texture.
        /// </summary>
        [Test]
        public void ADifferentPalette_GetsItsOwnBoardTile()
        {
            Palette other = Palette.Classic.Derive("test-other", p => p.Ground = Color.magenta);

            try
            {
                Sprite classic = ProceduralSprites.BoardTile();

                Palette.Use(other);
                Sprite derived = ProceduralSprites.BoardTile();

                Assert.AreNotSame(classic, derived,
                    "a second palette was drawn on the first one's board tile");
            }
            finally
            {
                Palette.Use(null);
            }
        }
    }
}
