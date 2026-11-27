// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuaDecompiler.Lua51
{
    /// <summary>
    /// Dominator tree for a control flow graph (iterative Cooper, Harvey, Kennedy algorithm).
    /// </summary>
    public sealed class DominatorTree
    {
        private const int Undef = -1;
        private readonly int[] _idom;
        private readonly List<int>[] _children;
        private readonly List<int>[] _frontier;
        public int NumBlocks { get; }

        private DominatorTree(int[] idom, List<int>[] children, List<int>[] frontier, int n)
        {
            _idom = idom; _children = children; _frontier = frontier; NumBlocks = n;
        }

        /// <summary>Build the dominator tree from a CFG. Entry block is block 0.</summary>
        public static DominatorTree Build(ControlFlowGraph cfg)
        {
            int n = cfg.NumBlocks;
            if (n == 0)
                return new DominatorTree(new int[0], new List<int>[0], new List<int>[0], 0);

            var (rpoOrder, rpoNum) = ComputeRpo(cfg, n);

            var idom = Enumerable.Repeat(Undef, n).ToArray();
            idom[0] = 0;

            int Intersect(int b1, int b2)
            {
                while (b1 != b2)
                {
                    while (rpoNum[b1] > rpoNum[b2]) b1 = idom[b1];
                    while (rpoNum[b2] > rpoNum[b1]) b2 = idom[b2];
                }
                return b1;
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 1; i < rpoOrder.Count; i++)
                {
                    int b = rpoOrder[i];
                    var preds = cfg.Blocks[b].Predecessors;
                    int newIdom = Undef;
                    foreach (int p in preds)
                    {
                        if (idom[p] != Undef) { newIdom = p; break; }
                    }
                    if (newIdom == Undef)
                        continue;
                    foreach (int p in preds)
                    {
                        if (p != newIdom && idom[p] != Undef)
                            newIdom = Intersect(p, newIdom);
                    }
                    if (idom[b] != newIdom)
                    {
                        idom[b] = newIdom;
                        changed = true;
                    }
                }
            }

            var children = new List<int>[n];
            for (int b = 0; b < n; b++) children[b] = new List<int>();
            for (int b = 0; b < n; b++)
            {
                if (idom[b] != b && idom[b] != Undef)
                    children[idom[b]].Add(b);
            }

            var frontier = ComputeDominanceFrontiers(idom, cfg, n);
            return new DominatorTree(idom, children, frontier, n);
        }

        public int IDom(int b) => _idom[b];

        public IReadOnlyList<int> Children(int b) => _children[b];

        public IReadOnlyList<int> Frontier(int b) => _frontier[b];

        /// <summary>Check if block a dominates block b.</summary>
        public bool Dominates(int a, int b)
        {
            int cur = b;
            while (true)
            {
                if (cur == a) return true;
                int id = _idom[cur];
                if (id == Undef || id == cur) return false;
                cur = id;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder("Dominator Tree:\n");
            for (int b = 0; b < NumBlocks; b++)
                sb.Append($"  B{b}: idom=B{_idom[b]}, children=[{string.Join(", ", _children[b])}], frontier=[{string.Join(", ", _frontier[b])}]\n");
            return sb.ToString();
        }

        private static (List<int> order, int[] number) ComputeRpo(ControlFlowGraph cfg, int n)
        {
            // Iterative DFS producing exactly the same postorder as the recursive version.
            var visited = new bool[n];
            var postorder = new List<int>(n);
            var stack = new Stack<(int block, int nextSucc)>();
            visited[0] = true;
            stack.Push((0, 0));
            while (stack.Count > 0)
            {
                var (b, i) = stack.Pop();
                var succs = cfg.Blocks[b].Successors;
                bool pushedChild = false;
                while (i < succs.Count)
                {
                    int s = succs[i++];
                    if (!visited[s])
                    {
                        visited[s] = true;
                        stack.Push((b, i));
                        stack.Push((s, 0));
                        pushedChild = true;
                        break;
                    }
                }
                if (!pushedChild)
                    postorder.Add(b);
            }

            postorder.Reverse();
            var number = new int[n];
            for (int i = 0; i < postorder.Count; i++)
                number[postorder[i]] = i;
            return (postorder, number);
        }

        private static List<int>[] ComputeDominanceFrontiers(int[] idom, ControlFlowGraph cfg, int n)
        {
            var frontiers = new List<int>[n];
            for (int i = 0; i < n; i++) frontiers[i] = new List<int>();

            for (int b = 0; b < n; b++)
            {
                var preds = cfg.Blocks[b].Predecessors;
                if (preds.Count >= 2)
                {
                    foreach (int p in preds)
                    {
                        int runner = p;
                        while (runner != idom[b] && runner != Undef)
                        {
                            if (!frontiers[runner].Contains(b))
                                frontiers[runner].Add(b);
                            if (runner == idom[runner])
                                break;
                            runner = idom[runner];
                        }
                    }
                }
            }
            return frontiers;
        }
    }

    /// <summary>Classification of Lua loop constructs.</summary>
    public enum LoopKind
    {
        NumericFor,
        GenericFor,
        WhileRepeat,
    }

    /// <summary>A natural loop identified from back-edges in the CFG.</summary>
    public sealed class NaturalLoop
    {
        public int Header;
        public int Latch;
        public List<int> Body;
        public LoopKind Kind;
    }

    public static class Loops
    {
        /// <summary>Find all natural loops in the CFG using dominator information.</summary>
        public static List<NaturalLoop> FindLoops(ControlFlowGraph cfg, DominatorTree dom)
        {
            var loops = new List<NaturalLoop>();
            foreach (var edge in cfg.Edges)
            {
                var targetBlock = cfg.Blocks[edge.To];
                var endOp = cfg.Instructions[targetBlock.End].Op;
                bool targetEndsWithForloop = endOp == OpCode.ForLoop || endOp == OpCode.TForLoop;
                bool isExplicitLoopBack = edge.Kind == EdgeKind.ForLoopBack || edge.Kind == EdgeKind.TForLoopBack;
                bool isNaturalBackEdge = dom.Dominates(edge.To, edge.From) && !(targetEndsWithForloop && !isExplicitLoopBack);

                if (isExplicitLoopBack || isNaturalBackEdge)
                {
                    int header = edge.To;
                    int latch = edge.From;
                    var body = CollectLoopBody(cfg, header, latch);
                    if (edge.Kind == EdgeKind.ForLoopBack)
                        body.RemoveAll(id => cfg.Instructions[cfg.Blocks[id].End].Op == OpCode.ForPrep);
                    loops.Add(new NaturalLoop { Header = header, Latch = latch, Body = body, Kind = ClassifyLoop(cfg, edge.Kind, header) });
                }
            }
            // Stable sort, like Rust's sort_by_key.
            return loops.OrderBy(l => l.Header).ThenBy(l => l.Latch).ToList();
        }

        private static List<int> CollectLoopBody(ControlFlowGraph cfg, int header, int latch)
        {
            var body = new bool[cfg.NumBlocks];
            body[header] = true;
            if (header == latch)
                return new List<int> { header };

            body[latch] = true;
            var stack = new Stack<int>();
            stack.Push(latch);
            while (stack.Count > 0)
            {
                int b = stack.Pop();
                foreach (int pred in cfg.Blocks[b].Predecessors)
                {
                    if (!body[pred])
                    {
                        body[pred] = true;
                        stack.Push(pred);
                    }
                }
            }
            var result = new List<int>();
            for (int i = 0; i < body.Length; i++)
                if (body[i]) result.Add(i);
            return result;
        }

        private static LoopKind ClassifyLoop(ControlFlowGraph cfg, EdgeKind edgeKind, int header)
        {
            switch (edgeKind)
            {
                case EdgeKind.ForLoopBack: return LoopKind.NumericFor;
                case EdgeKind.TForLoopBack: return LoopKind.GenericFor;
                default:
                    var block = cfg.Blocks[header];
                    for (int pc = block.Start; pc <= block.End; pc++)
                    {
                        switch (cfg.Instructions[pc].Op)
                        {
                            case OpCode.ForLoop: return LoopKind.NumericFor;
                            case OpCode.TForLoop: return LoopKind.GenericFor;
                        }
                    }
                    return LoopKind.WhileRepeat;
            }
        }
    }
}
