using System;
using System.Collections;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The lookups the workbench makes every frame walk their lists by index (Part 16e's performance pass). A module by its
    /// name -- the module shown, whether it is written, whether it shows its code -- and a part or a wire by its id or name are
    /// asked dozens of times a still frame, and each walked its list with <c>foreach</c> over the interface. Unity's Mono and
    /// IL2CPP put that enumerator on the heap: 32 to 40 bytes a lookup, about 5 KB a still frame over a written sample with the
    /// pointer on the circuit. .NET 10 can keep such an enumerator on the stack, so an allocation measured here would not show
    /// it: what is held is the walk itself, which never asks for an enumerator.
    /// </summary>
    public class LookupCostTests
    {
        /// <summary>A list that can be read by index and refuses to be walked with an enumerator.</summary>
        private sealed class IndexedOnly<T> : IReadOnlyList<T>
        {
            private readonly T[] _items;

            public IndexedOnly(params T[] items) => _items = items;

            public T this[int index] => _items[index];
            public int Count => _items.Length;

            public IEnumerator<T> GetEnumerator() =>
                throw new InvalidOperationException("walked through an enumerator, which Mono and IL2CPP allocate on the heap");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static Project TwoModules() => new Project
        {
            Name = "lookups",
            Top = "top",
            Modules = new IndexedOnly<Module>(
                new Module { Name = "inner" },
                new Module
                {
                    Name = "top",
                    Parts = new IndexedOnly<Part>(
                        new PortPart { Id = 1, Name = "a" },
                        new PortPart { Id = 2, Name = "y", Direction = PortDirection.Output }),
                    Wires = new IndexedOnly<Wire>(new Wire { Id = 3 }),
                }),
        };

        [Test]
        public void AModule_IsFoundByIndex()
        {
            Project project = TwoModules();

            Assert.AreEqual("top", project.Find("top")?.Name);
            Assert.AreEqual("top", project.TopModule?.Name);
            Assert.IsNull(project.Find("none"), "a name no module has, the whole list walked");
        }

        [Test]
        public void APartAndAWire_AreFoundByIndex()
        {
            Module top = TwoModules().Find("top");

            Assert.AreEqual("y", top.FindPart(2)?.Name);
            Assert.AreEqual(2, top.FindPart("y")?.Id);
            Assert.AreEqual(3, top.FindWire(3)?.Id);
            Assert.IsNull(top.FindPart(99));
            Assert.IsNull(top.FindPart("none"));
            Assert.IsNull(top.FindWire(99));
        }

        [Test]
        public void TheFirstOfTwoAlike_IsTheOneFound()
        {
            // Found by index as by enumerator: the first in the list, as before.
            var first = new Module { Name = "m", Comment = "first" };
            var project = new Project { Top = "m", Modules = new IndexedOnly<Module>(first, new Module { Name = "m", Comment = "second" }) };

            Assert.AreSame(first, project.Find("m"));
        }
    }
}
