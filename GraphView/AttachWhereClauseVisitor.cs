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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GraphView;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace GraphView
{
    /// <summary>
    /// BindTableVisitor traverses a boolean expression and returns exposed name of a table
    /// if all columns involved in the expression are from that table, otherwise it returns 
    /// an empty string.
    /// </summary>
    internal class BindTableVisitor : WSqlFragmentVisitor
    {
        private string _tableName;

        private bool _tableBind;

        private Dictionary<string, string> _columnToTable;



        private void Bind(string table)
        {
            if (_tableBind)
            {
                if (!string.IsNullOrEmpty(_tableName) && table != _tableName)
                    _tableName = "";
            }
            else
            {
                _tableName = table;
                _tableBind = true;
            }
        }

        public string Invoke(WBooleanExpression node, Dictionary<string, string> columnToTable)
        {
            _tableBind = false;
            _columnToTable = columnToTable;
            node.Accept(this);
            return _tableName;
        }

        public override void Visit(WColumnReferenceExpression node)
        {
            var column = node.MultiPartIdentifier.Identifiers;
            if (column.Count >= 2)
            {
                Bind(column[column.Count - 2].Value);
            }
            else
            {
                var columnName = column.Last().Value;
                Bind(_columnToTable.ContainsKey(columnName) ?
                    _columnToTable[columnName] :
                    "");
            }
        }

        public override void Visit(WScalarSubquery node)
        {
        }

        public override void Visit(WFunctionCall node)
        {
        }

        public override void Visit(WSearchedCaseExpression node)
        {
        }
    }

    /// <summary>
    /// AttachWhereClauseVisitor traverses a WHERE clause and attachs predicates
    /// into nodes and edges of constructed graph.
    /// </summary>
    internal class AttachWhereClauseVisitor : WSqlFragmentVisitor
    {
        private MatchGraph _graph;
        private Dictionary<string, string> _columnTableMapping;
        private readonly BindTableVisitor _bindTableVisitor = new BindTableVisitor();

        public void Invoke(WWhereClause node, MatchGraph graph, Dictionary<string, string> columnTableMapping)
        {
            _graph = graph;
            _columnTableMapping = columnTableMapping;

            if (node.SearchCondition != null)
                node.SearchCondition.Accept(this);
        }

        private void Attach(WBooleanExpression expr)
        {
            var table = _bindTableVisitor.Invoke(expr,_columnTableMapping);

            MatchEdge edge;
            MatchNode node;
            if (_graph.TryGetEdge(table,out edge))
            {
                if (edge.Predicates == null)
                {
                    edge.Predicates = new List<WBooleanExpression>();
                }
                edge.Predicates.Add(expr);
            }
            else if (_graph.TryGetNode(table,out node))
            {
                if (node.Predicates == null)
                {
                    node.Predicates = new List<WBooleanExpression>();
                }
                node.Predicates.Add(expr);
            }

        }

        public override void Visit(WBooleanBinaryExpression node)
        {
            if (node.BooleanExpressionType == BooleanBinaryExpressionType.And)
            {
                base.Visit(node);
            }
            else
            {
                Attach(node);
            }
        }

        public override void Visit(WBooleanComparisonExpression node)
        {
            Attach(node);
        }

        public override void Visit(WBooleanIsNullExpression node)
        {
            Attach(node);
        }

        public override void Visit(WBetweenExpression node)
        {
            Attach(node);
        }

        public override void Visit(WLikePredicate node)
        {
            Attach(node);
        }

        public override void Visit(WInPredicate node)
        {
        }

        public override void Visit(WSubqueryComparisonPredicate node)
        {
        }

        public override void Visit(WExistsPredicate node)
        {
        }
    }


    internal class CheckNodeEdgeReferenceVisitor : WSqlFragmentVisitor
    {
        private bool _referencedByNodeAndEdge;
        private MatchGraph _graph;
        private Dictionary<string, string> _columnTableMapping;

        public CheckNodeEdgeReferenceVisitor(MatchGraph graph, Dictionary<string, string> columnTableMapping)
        {
            _graph = graph;
            _columnTableMapping = columnTableMapping;
        }
        public bool Invoke(WBooleanExpression node)
        {
            _referencedByNodeAndEdge = true;
            node.Accept(this);
            return _referencedByNodeAndEdge;
        }
        public override void Visit(WColumnReferenceExpression node)
        {
            if (!_referencedByNodeAndEdge) 
                return;
            var column = node.MultiPartIdentifier.Identifiers;
            string tableAlias = "";
            if (column.Count >= 2)
            {
                tableAlias = column[column.Count - 2].Value;
            }
            else
            {
                var columnName = column.Last().Value;
                if ((_columnTableMapping.ContainsKey(columnName)))
                {
                    tableAlias = _columnTableMapping[columnName];
                }
            }

            if (!_graph.ContainsNode(tableAlias))
            {
                _referencedByNodeAndEdge = false;
            }
        }

        public override void Visit(WScalarSubquery node)
        {
            _referencedByNodeAndEdge = false;
        }

        public override void Visit(WFunctionCall node)
        {
            _referencedByNodeAndEdge = false;
        }

        public override void Visit(WSearchedCaseExpression node)
        {
            _referencedByNodeAndEdge = false;
        }
    }

    internal class AttachNodeEdgePredictesVisitor : WSqlFragmentVisitor
    {

        private CheckNodeEdgeReferenceVisitor _checkNodeEdgeReferenceVisitor;
        private WWhereClause _nodeEdgePredicatesWhenClause = new WWhereClause();

        public WWhereClause Invoke(WWhereClause node, MatchGraph graph, Dictionary<string, string> columnTableMapping)
        {
            _checkNodeEdgeReferenceVisitor = new CheckNodeEdgeReferenceVisitor(graph, columnTableMapping)
            ;
            if (node.SearchCondition != null)
                node.SearchCondition.Accept(this);
            return _nodeEdgePredicatesWhenClause;
        }

        public void UpdateWherClause(WWhereClause whereClause, WBooleanExpression node)
        {
            if (whereClause.SearchCondition == null)
                whereClause.SearchCondition = node;
            else
            {
                whereClause.SearchCondition = new WBooleanBinaryExpression
                {
                    FirstExpr = whereClause.SearchCondition,
                    SecondExpr = node,
                    BooleanExpressionType = BooleanBinaryExpressionType.And
                };
            }
        }

        public override void Visit(WBooleanBinaryExpression node)
        {
            if (node.BooleanExpressionType == BooleanBinaryExpressionType.And)
            {
                if (_checkNodeEdgeReferenceVisitor.Invoke(node.FirstExpr))
                {
                    UpdateWherClause(_nodeEdgePredicatesWhenClause, node.FirstExpr);
                }
                else
                {
                    base.Visit(node.FirstExpr);
                }
                if (_checkNodeEdgeReferenceVisitor.Invoke(node.SecondExpr))
                {
                    UpdateWherClause(_nodeEdgePredicatesWhenClause, node.SecondExpr);
                }
                else
                {
                    base.Visit(node.SecondExpr);
                }
            }
            else
            {
                if (_checkNodeEdgeReferenceVisitor.Invoke(node))
                {
                    UpdateWherClause(_nodeEdgePredicatesWhenClause,node);
                }
            }
        }

        public override void Visit(WBooleanComparisonExpression node)
        {
            if (_checkNodeEdgeReferenceVisitor.Invoke(node))
            {
                UpdateWherClause(_nodeEdgePredicatesWhenClause, node);
            }
        }

        public override void Visit(WBooleanIsNullExpression node)
        {
            if (_checkNodeEdgeReferenceVisitor.Invoke(node))
            {
                UpdateWherClause(_nodeEdgePredicatesWhenClause, node);
            }
        }

        public override void Visit(WBetweenExpression node)
        {
            if (_checkNodeEdgeReferenceVisitor.Invoke(node))
            {
                UpdateWherClause(_nodeEdgePredicatesWhenClause, node);
            }
        }

        public override void Visit(WLikePredicate node)
        {
            if (_checkNodeEdgeReferenceVisitor.Invoke(node))
            {
                UpdateWherClause(_nodeEdgePredicatesWhenClause, node);
            }
        }

        public override void Visit(WInPredicate node)
        {
        }

        public override void Visit(WSubqueryComparisonPredicate node)
        {
        }

        public override void Visit(WExistsPredicate node)
        {
        }
    }

    
}
