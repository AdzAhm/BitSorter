using System.Collections;
using BitSorter.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Reading the keyboard (<see cref="UiText.Keyboard"/>), which every key reader of the workbench does every frame, makes no
    /// garbage whatever the event system has selected (the zero-GC pass, Part 16) -- and still sees a field being typed in.
    /// </summary>
    /// <remarks>
    /// The event system's selection is mostly no field: a click on the shell makes its panel's object the selection, and it
    /// stays so. Asking that object for a TextMesh Pro field with <c>GetComponent</c> made, in the editor, an object for the
    /// field that was not there, once for every reader every frame; <c>TryGetComponent</c> makes none. The pair: the same read
    /// with a field selected says a field is being typed in, so the first test's quiet is not a read that sees nothing.
    /// </remarks>
    [TestFixture]
    public class KeyboardReadPlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            yield return TestScene.Clear();
        }

        [UnityTest]
        public IEnumerator ReadingTheKeyboard_WithSomethingButAFieldSelected_AllocatesNothing()
        {
            yield return TestScene.Load();
            Assert.IsNotNull(EventSystem.current, "sanity: the scene has its event system");

            var plain = new GameObject("selected, and no field");
            try
            {
                EventSystem.current.SetSelectedGameObject(plain);
                Assert.AreSame(plain, EventSystem.current.currentSelectedGameObject, "sanity: selected");
                Assert.IsFalse(UiText.Typing, "nothing is being typed in");

                Assert.That(() =>
                {
                    for (int read = 0; read < 100; read++)
                        _ = UiText.Keyboard;
                }, Is.Not.AllocatingGCMemory(), "a hundred reads of the keyboard, something but a field selected");
            }
            finally
            {
                EventSystem.current.SetSelectedGameObject(null);
                Object.Destroy(plain);
            }
        }

        [UnityTest]
        public IEnumerator ReadingTheKeyboard_WithAFieldSelected_SeesItTypedIn()
        {
            yield return TestScene.Load();
            Assert.IsNotNull(EventSystem.current, "sanity: the scene has its event system");

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            var holder = new GameObject("a field", typeof(RectTransform));
            holder.transform.SetParent(canvas.transform, false);
            var field = holder.AddComponent<TMP_InputField>();
            try
            {
                EventSystem.current.SetSelectedGameObject(holder);
                Assert.IsTrue(UiText.Typing, "a field selected is being typed in");
                Assert.IsNull(UiText.Keyboard, "and the game's keys see no keyboard");

                field.enabled = false;
                Assert.IsFalse(UiText.Typing, "a field switched off is not");
            }
            finally
            {
                EventSystem.current.SetSelectedGameObject(null);
                Object.Destroy(holder);
            }
        }
    }
}
