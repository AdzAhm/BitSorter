namespace BitSorter.SystemVerilog
{
    /// <summary>What a wire reads when more than one thing drives it.</summary>
    public readonly partial struct LogicVec
    {
        /// <summary>
        /// Two drivers of one <c>wire</c>, resolved bit by bit (IEEE 1800, 6.6.1): a driver that is z
        /// gives way to the other; two that agree give their value; two that disagree give x. 0
        /// against 1 is the course's "two drivers on a net" mistake, and it reads x.
        /// </summary>
        /// <remarks>
        /// Only for nets. A variable -- anything declared <c>logic</c> -- may have one continuous
        /// driver at most (IEEE 1800, 6.5), and Icarus refuses to compile two (Docs/oracle-notes.md).
        /// Strengths are not modelled: every driver drives at full strength, as every one the course
        /// writes does.
        /// </remarks>
        public static LogicVec ResolveWire(LogicVec p, LogicVec q)
        {
            Require(p);
            Require(q);

            int width = System.Math.Max(p._width, q._width);
            p = p.Extend(width, false);
            q = q.Extend(width, false);

            if (width <= 64)
            {
                ResolveWord(p._a, p._b, q._a, q._b, out ulong ra, out ulong rb);
                return new LogicVec(width, false, ra, rb);
            }

            int n = WordsFor(width);
            ulong[] planes = NewPlanes(width);
            for (int i = 0; i < n; i++)
                ResolveWord(p._wide[i], p._wide[n + i], q._wide[i], q._wide[n + i], out planes[i], out planes[n + i]);

            return new LogicVec(width, false, planes);
        }

        private static void ResolveWord(ulong pa, ulong pb, ulong qa, ulong qb, out ulong ra, out ulong rb)
        {
            ulong pz = ~pa & pb, qz = ~qa & qb;
            ulong same = ~((pa ^ qa) | (pb ^ qb));

            ulong takeQ = pz;                       // p is z: q's bit, whatever it is
            ulong takeP = ~pz & (qz | same);        // q is z, or they agree: p's bit
            ulong clash = ~pz & ~qz & ~same;        // two values that disagree

            ra = (takeQ & qa) | (takeP & pa) | clash;
            rb = (takeQ & qb) | (takeP & pb) | clash;
        }
    }
}
