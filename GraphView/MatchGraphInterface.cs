﻿// GraphView
// 
// Copyright (c) 2015 Microsoft Corporation
// 
// All rights reserved. 
// 
// MIT License
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    // TODO: do some pruning, deal with unpopulated & populated leaf to leaf
    /// <summary>
    /// 
    /// </summary>
    internal interface IMatchJoinPruning
    {
        IEnumerable<OneHeightTree> GetCandidateUnits(IEnumerable<Tuple<OneHeightTree, bool>> treeTuples, MatchComponent component);
    }

    internal class PruneJointEdge : IMatchJoinPruning
    {
        public IEnumerable<OneHeightTree> GetCandidateUnits(IEnumerable<Tuple<OneHeightTree, bool>> treeTuples, MatchComponent component)
        {
            foreach (var treeTuple in treeTuples)
            {
                var tree = treeTuple.Item1;
                bool singleNode = treeTuple.Item2;
                var root = tree.TreeRoot;
                var jointEdges = tree.MaterializedEdges;
                var unpopEdges = tree.UnmaterializedEdges;
                int joinEdgesCount = jointEdges.Count;
                int unpopEdgesCount = unpopEdges.Count;

                int num = (int)Math.Pow(2, joinEdgesCount) - 1;
                while (num >= 0)
                {
                    if (joinEdgesCount>0 && num==0) break;
                    var newJointEdges = new List<MatchEdge>();
                    for (int i = 0; i < joinEdgesCount; i++)
                    {
                        int index = (1 << i);
                        if ((num & index) != 0)
                        {
                            var edge = jointEdges[i];
                            newJointEdges.Add(edge);
                        }
                    }

                    int eNum = (int)Math.Pow(2, unpopEdgesCount) - 1;
                    while (eNum >= 0)
                    {
                        if (eNum == 0 && singleNode) break;
                        var newUnpopEdges = new List<MatchEdge>();
                        for (int i = 0; i < unpopEdgesCount; i++)
                        {
                            int index = (1 << i);
                            if ((eNum & index) != 0)
                            {
                                newUnpopEdges.Add(unpopEdges[i]);
                            }
                        }
                        
                        yield return new OneHeightTree
                        {
                            TreeRoot = root,
                            MaterializedEdges = newJointEdges,
                            UnmaterializedEdges = newUnpopEdges,
                        };
                        eNum--;
                    }
                    num--;
                }
            }
        }
    }

    internal class MatchEdgeTupleEqualityComparer : IEqualityComparer<Tuple<string, string>>
    {
        public bool Equals(Tuple<string, string> x, Tuple<string, string> y)
        {
            if ((x.Item1 == y.Item1 && x.Item2 == y.Item2) || (x.Item1 == y.Item2 && x.Item2 == y.Item1))
                return true;
            return false;
        }

        public int GetHashCode(Tuple<string, string> obj)
        {
            String str = obj.Item2 + obj.Item1;
            if (String.CompareOrdinal(obj.Item1, obj.Item2) > 0)
                str = obj.Item1 + obj.Item2;
            return str.GetHashCode();
        }
    }

    internal interface IMatchJoinStatisticsCalculator
    {
        WSqlTableContext Context { get; set; }

        ColumnStatistics GetLeafToLeafStatistics(MatchEdge nodeEdge, MatchEdge componentEdge);


    }

    internal class HistogramCalculator : IMatchJoinStatisticsCalculator
    {
        public Dictionary<Tuple<string, string>, ColumnStatistics> LeafToLeafSelectivity { get; set; }

        public HistogramCalculator()
        {
            LeafToLeafSelectivity = new Dictionary<Tuple<string, string>, ColumnStatistics>(new MatchEdgeTupleEqualityComparer());
        }

        public WSqlTableContext Context { get; set; }
        public ColumnStatistics GetLeafToLeafStatistics(MatchEdge nodeEdge, MatchEdge componentEdge)
        {
            var edgeTuple = new Tuple<string, string>(nodeEdge.EdgeAlias, componentEdge.EdgeAlias);

            if (LeafToLeafSelectivity.ContainsKey(edgeTuple))
                return LeafToLeafSelectivity[edgeTuple];

            var mergedStatistics = ColumnStatistics.UpdateHistogram(Context.GetEdgeStatistics(nodeEdge), Context.GetEdgeStatistics(componentEdge));
            LeafToLeafSelectivity[edgeTuple] = mergedStatistics;
            return mergedStatistics;
        }

    }
}
