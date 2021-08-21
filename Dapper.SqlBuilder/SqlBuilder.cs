using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dapper
{
    public class SqlBuilder
    {
        private readonly Dictionary<string, DefaultStatementBuilder> _data = new Dictionary<string, DefaultStatementBuilder>();
        private WhereStatementBuilder _whereBuilder;

        private int _seq;

        private interface IClause
        {
            string Sql { get; set; }
            object Parameters { get; set; }
        }

        private class Clause : IClause
        {
            public string Sql { get; set; }
            public object Parameters { get; set; }
        }

        private class WhereClause : IClause
        {
            public string Joiner { get; set; }
            public string Sql { get; set; }
            public object Parameters { get; set; }
        }

        private interface IStatementBuilder
        {
            string Resolve(DynamicParameters p);
        }

        private abstract class SqlStatementBuilder<T> : IStatementBuilder
            where T : IClause
        {
            protected SqlStatementBuilder(string prefix, string postfix)
            {
                Clauses = new List<T>();
                Prefix = prefix;
                Postfix = postfix;
            }

            protected List<T> Clauses { get; }
            protected string Prefix { get; }
            protected string Postfix { get; }

            public abstract string Resolve(DynamicParameters p);

            public void Append(T clause) => 
                Clauses.Add(clause);
        }

        private sealed class WhereStatementBuilder : SqlStatementBuilder<WhereClause>
        {
            public WhereStatementBuilder() :
                base("WHERE ", "\n")
            { }

            public override string Resolve(DynamicParameters p)
            {
                var whereStatement = new StringBuilder();
                whereStatement.Append(Prefix);

                for (var i = 0; i < Clauses.Count; i++)
                {
                    var clause = Clauses[i];

                    p.AddDynamicParams(clause.Parameters);

                    if (i > 0)
                        whereStatement.Append(clause.Joiner);

                    whereStatement.Append(clause.Sql);
                }

                return whereStatement.Append(Postfix).ToString();
            }
        }

        private sealed class DefaultStatementBuilder : SqlStatementBuilder<Clause>
        {
            private readonly string _joiner;

            public DefaultStatementBuilder(string joiner, string prefix, string postfix) : base(prefix, postfix)
                => _joiner = joiner;

            public override string Resolve(DynamicParameters p)
            {
                foreach (var clause in Clauses)
                    p.AddDynamicParams(clause.Parameters);

                return $"{ Prefix }{string.Join(_joiner, Clauses.Select(c => c.Sql).ToArray())}{ Postfix }";
            }
        }

        public class Template
        {
            private readonly string _sql;
            private readonly SqlBuilder _builder;
            private readonly object _initParams;
            private int _dataSeq = -1; // Unresolved

            public Template(SqlBuilder builder, string sql, dynamic parameters)
            {
                _initParams = parameters;
                _sql = sql;
                _builder = builder;
            }

            private static readonly Regex _regex = new Regex(@"\/\*\*.+?\*\*\/", RegexOptions.Compiled | RegexOptions.Multiline);

            private void ResolveSql()
            {
                if (_dataSeq != _builder._seq)
                {
                    var p = new DynamicParameters(_initParams);

                    rawSql = _sql;

                    foreach (var pair in _builder.Data)
                        rawSql = rawSql.Replace($"/**{pair.Key}**/", pair.Value.Resolve(p));

                    parameters = p;

                    // replace all that is left with empty
                    rawSql = _regex.Replace(rawSql, "");

                    _dataSeq = _builder._seq;
                }
            }

            private string rawSql;
            private object parameters;

            public string RawSql
            {
                get { ResolveSql(); return rawSql; }
            }

            public object Parameters
            {
                get { ResolveSql(); return parameters; }
            }
        }

        private IEnumerable<KeyValuePair<string, IStatementBuilder>> Data
        {
            get
            {
                foreach (var item in _data)
                    yield return new KeyValuePair<string, IStatementBuilder>(item.Key, item.Value);
                yield return new KeyValuePair<string, IStatementBuilder>("where", _whereBuilder);
            }
        }

        public Template AddTemplate(string sql, dynamic parameters = null) =>
            new Template(this, sql, parameters);

        protected SqlBuilder AddClause(string name, string sql, object parameters, string joiner, string prefix = "", string postfix = "")
        {
            if (name == "where")
            {
                _whereBuilder ??= new WhereStatementBuilder();
                _whereBuilder.Append(new WhereClause { Sql = sql, Parameters = parameters, Joiner = joiner });
            }
            else
            {
                if (!_data.TryGetValue(name, out var _))
                    _data[name] = new DefaultStatementBuilder(joiner, prefix, postfix);

                _data[name].Append(new Clause { Sql = sql, Parameters = parameters });
            }

            _seq++;
            return this;
        }

        public SqlBuilder Intersect(string sql, dynamic parameters = null) =>
            AddClause("intersect", sql, parameters, "\nINTERSECT\n ", "\n ", "\n");

        public SqlBuilder InnerJoin(string sql, dynamic parameters = null) =>
            AddClause("innerjoin", sql, parameters, "\nINNER JOIN ", "\nINNER JOIN ", "\n");

        public SqlBuilder LeftJoin(string sql, dynamic parameters = null) =>
            AddClause("leftjoin", sql, parameters, "\nLEFT JOIN ", "\nLEFT JOIN ", "\n");

        public SqlBuilder RightJoin(string sql, dynamic parameters = null) =>
            AddClause("rightjoin", sql, parameters, "\nRIGHT JOIN ", "\nRIGHT JOIN ", "\n");

        public SqlBuilder Where(string sql, dynamic parameters = null) =>
            AddClause("where", sql, parameters, " AND ");

        public SqlBuilder OrWhere(string sql, dynamic parameters = null) =>
            AddClause("where", sql, parameters, " OR ");

        public SqlBuilder OrderBy(string sql, dynamic parameters = null) =>
            AddClause("orderby", sql, parameters, " , ", "ORDER BY ", "\n");

        public SqlBuilder Select(string sql, dynamic parameters = null) =>
            AddClause("select", sql, parameters, " , ", "", "\n");

        public SqlBuilder AddParameters(dynamic parameters) =>
            AddClause("--parameters", "", parameters, "", "", "");

        public SqlBuilder Join(string sql, dynamic parameters = null) =>
            AddClause("join", sql, parameters, "\nJOIN ", "\nJOIN ", "\n");

        public SqlBuilder GroupBy(string sql, dynamic parameters = null) =>
            AddClause("groupby", sql, parameters, " , ", "\nGROUP BY ", "\n");

        public SqlBuilder Having(string sql, dynamic parameters = null) =>
            AddClause("having", sql, parameters, "\nAND ", "HAVING ", "\n");

        public SqlBuilder Set(string sql, dynamic parameters = null) =>
            AddClause("set", sql, parameters, " , ", "SET ", "\n");
    }
}
