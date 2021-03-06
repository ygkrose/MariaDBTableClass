using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fclp;
using MySql.Data.MySqlClient;

namespace MySQL_To_CSharp
{

    public class ApplicationArguments
    {
        public string IP { get; set; } 
        public int Port { get; set; } 
        public string User { get; set; }
        public string Password { get; set; } 
        public string Database { get; set; } 
        public string Table { get; set; }
        public bool GenerateConstructorAndOutput { get; set; } 
        public bool GenerateMarkupPages { get; set; } 
        public string MarkupDatabaseNameReplacement { get; set; }
    }

    public class Column
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public string ColumnType { get; set; }

        public bool AllowNull { get; set; }

        public bool KeyColumn { get; set; }

        public string Comment { get; set; }

        public string Default { get; set; }

        public Column(MySqlDataReader reader)
        {
            this.Name = reader.GetString(1);
            this.ColumnType = reader.GetString(2);
            this.AllowNull = reader["IS_NULLABLE"].ToString() == "YES" ? true : false;
            this.KeyColumn = reader["COLUMN_KEY"].ToString() == "PRI" ? true : false;
            this.Comment = reader["COLUMN_COMMENT"].ToString().Trim();
        }

        public override string ToString()
        {
            string tpy = this.Type.Name == "String" ? "string" : this.Type.Name;
            return $"public {tpy} {this.Name.FirstCharUpper()} {{ get; set; }}";
        }
    }

    public static class StringExtension
    {
        public static string FirstCharUpper(this string str)
        {
            return str.First().ToString().ToUpper() + str.Substring(1);
        }
    }

  

    class Program
    {
        private static bool IsPrjTable(string tabname)
        {
            if (Regex.IsMatch(tabname.Substring(0, 1), "[A-Z]") &&
                Regex.IsMatch(tabname.Substring(1, 1), "[A-Z]") &&
                Regex.IsMatch(tabname.Substring(2, 1), "[A-Z]") &&
                tabname.Substring(3, 1) == "_")
            {
                return true;
            }
            else
                return false;
        }

        private static void DbToClasses(string dbName, Dictionary<string, List<Column>> db, Dictionary<string, string> tc, bool generateConstructorAndOutput)
        {
            if (!Directory.Exists(dbName))
                Directory.CreateDirectory(dbName);

            var sb = new StringBuilder();
            foreach (var table in db)
            {
                var _tableprefix = table.Key.Split(new char[] { '_' });
                if (IsPrjTable(table.Key))
                {
                    if (!Directory.Exists($"{dbName}/" + _tableprefix[0]))
                        Directory.CreateDirectory($"{dbName}/" + _tableprefix[0]);
                }
                sb.AppendLine("using System;");
                sb.AppendLine("using NewCity.DataAccess.Model;");
                sb.AppendLine("using System.ComponentModel.DataAnnotations;");
                sb.AppendLine("");
                if (IsPrjTable(table.Key))
                    sb.AppendLine("namespace NewCity.DataAccess.Model."+ _tableprefix[0]);
                else
                    sb.AppendLine("namespace NewCity.DataAccess.Model");

                sb.AppendLine("{");
                //寫入class說明
                getTableComment(table.Key,sb,tc);
                sb.AppendLine($"public class {table.Key} : TableBase");
                sb.AppendLine("{");

                // properties
                foreach (Column col in table.Value)
                {
                    sb.AppendLine("");
                    if (col.Name.ToLower() == "seq" || col.Name.ToLower() == "id") continue;

                    if (!string.IsNullOrEmpty(col.Comment))
                    {
                        sb.AppendLine("///<summary>");
                        sb.AppendLine($"///{col.Comment}");
                        sb.AppendLine($"///[{col.ColumnType}, nullable({col.AllowNull})]");
                        sb.AppendLine("///</summary>");
                    }
                    if (col.Name.ToLower() == "seq") 
                       sb.AppendLine("[IgnoreDataMember]");
                    if (col.Name.ToLower() == "id") 
                       sb.AppendLine("[Key]");
                    if (!col.AllowNull)
                        sb.AppendLine("[Required]");
                    if (col.AllowNull && col.Type == typeof(Guid))
                        sb.AppendLine($"public Guid? {col.Name} {{ get; set; }}");
                    else if (col.AllowNull && col.Type == typeof(DateTime))
                        sb.AppendLine($"public DateTime? {col.Name} {{ get; set; }}");
                    else if (col.AllowNull && col.Type == typeof(Byte))
                        sb.AppendLine($"public Byte? {col.Name} {{ get; set; }}");
                    else
                        sb.AppendLine(col.ToString());
                }

                sb.AppendLine("}");

                if (generateConstructorAndOutput)
                {
                    // constructor
                    sb.AppendLine($"{Environment.NewLine}public {table.Key}(MySqlDataReader reader)");
                    sb.AppendLine("{");
                    foreach (var column in table.Value)
                    {
                        // check which type and use correct get method instead of casting
                        if (column.Type != typeof(string))
                            sb.AppendLine($"{column.Name.FirstCharUpper()} = Convert.To{column.Type.Name}(reader[\"{column.Name}\"].ToString());");
                        else
                            sb.AppendLine($"{column.Name.FirstCharUpper()} = reader[\"{column.Name}\"].ToString();");
                    }
                    sb.AppendLine($"}}{Environment.NewLine}");

                    // update query
                    sb.AppendLine($"public string UpdateQuery()");
                    sb.AppendLine("{");
                    sb.Append($"return $\"UPDATE `{table.Key}` SET");
                    foreach (var column in table.Value)
                        sb.Append($" {column.Name} = {{{column.Name.FirstCharUpper()}}},");
                    sb.Remove(sb.ToString().LastIndexOf(','), 1);
                    sb.AppendLine($" WHERE {table.Value[0].Name} = {{{table.Value[0].Name.FirstCharUpper()}}};\";");
                    sb.AppendLine($"}}{Environment.NewLine}");

                    // insert query
                    sb.AppendLine($"public string InsertQuery()");
                    sb.AppendLine("{");
                    sb.Append($"return $\"INSERT INTO `{table.Key}` VALUES (");
                    foreach (var column in table.Value)
                        sb.Append($" {{{column.Name.FirstCharUpper()}}},");
                    sb.Remove(sb.ToString().LastIndexOf(','), 1);
                    sb.AppendLine($");\";{Environment.NewLine}}}{Environment.NewLine}");

                    // delete query
                    sb.AppendLine($"public string DeleteQuery()");
                    sb.AppendLine("{");
                    sb.AppendLine($"return $\"DELETE FROM `{table.Key}` WHERE {table.Value[0].Name} = {{{table.Value[0].Name.FirstCharUpper()}}};\";");
                    sb.AppendLine("}");
                }

                // class closing
                sb.AppendLine("}");
                var filename = $"{dbName}/{table.Key}.cs";
                if (IsPrjTable(table.Key))
                    filename = $"{dbName}/{_tableprefix[0]}/{table.Key}.cs";
                var sw = new StreamWriter(filename , false);
                sw.Write(sb.ToString());
                sw.Close();
                sb.Clear();
            }
        }

        private static void getTableComment(string key, StringBuilder sb, Dictionary<string, string> tc)
        {
            if (tc.ContainsKey(key))
            {
                sb.AppendLine("/// <summary>");
                sb.AppendLine($"///{tc[key]}");
                sb.AppendLine("/// </summary>");
            }
        }

        private static void DbToMarkupPage(string dbName, Dictionary<string, List<Column>> db)
        {
            var wikiDir = $"wiki";
            var wikiDbDir = $"{wikiDir}/{dbName}";
            var wikiTableDir = $"{wikiDbDir}/tables";

            if (!Directory.Exists(wikiDir))
                Directory.CreateDirectory(wikiDir);
            if (!Directory.Exists(wikiTableDir))
                Directory.CreateDirectory(wikiTableDir);

            var sb = new StringBuilder();

            sb.AppendLine($"* [[{dbName}|{dbName}]]");

            var sw = new StreamWriter($"{wikiDir}/index.txt", true);
            sw.Write(sb.ToString());
            sw.Close();
            sb.Clear();

            sb.AppendLine($"[[Database Structure|Database Structure]] > [[{dbName}|{dbName}]]");

            // generate index pages
            foreach (var table in db)
                sb.AppendLine($"* [[{table.Key.FirstCharUpper()}|{table.Key.ToLower()}]]");

            sw = new StreamWriter($"{wikiDbDir}/{dbName}.txt");
            sw.Write(sb.ToString());
            sw.Close();
            sb.Clear();

            foreach (var table in db)
            {
                sb.AppendLine($"[[Database Structure|Database Structure]] > [[{dbName}|{dbName}]] > [[{table.Key}|{table.Key}]]");
                sb.AppendLine("");
                sb.AppendLine("Column | Type | Description");
                sb.AppendLine("--- | --- | ---");

                foreach (var column in table.Value)
                    sb.AppendLine($"{column.Name.FirstCharUpper()} | {column.ColumnType} | ");
                sw = new StreamWriter($"{wikiTableDir}/{table.Key}.txt");
                sw.Write(sb.ToString());
                sw.Close();
                sb.Clear();
            }

        }

        static void Main(string[] args)
        {
            var parser = new FluentCommandLineParser<ApplicationArguments>();
            parser.Setup(arg => arg.IP).As('i', "ip").SetDefault("127.0.0.1").WithDescription("(optional) IP address of the MySQL server, will use 127.0.0.1 if not specified");
            parser.Setup(arg => arg.Port).As('n', "port").SetDefault(3306).WithDescription("(optional) Port number of the MySQL server, will use 3306 if not specified");
            parser.Setup(arg => arg.User).As('u', "user").SetDefault("root").WithDescription("(optional) Username, will use root if not specified");
            parser.Setup(arg => arg.Password).As('p', "password").SetDefault(String.Empty).WithDescription("(optional) Password, will use empty password if not specified");
            parser.Setup(arg => arg.Database).As('d', "database").Required().WithDescription("Database name");
            parser.Setup(arg => arg.Table).As('t', "table").SetDefault(String.Empty).WithDescription("(optional) Table name, will generate entire database if not specified");
            parser.Setup(arg => arg.GenerateConstructorAndOutput).As('g', "generateconstructorandoutput")
                .SetDefault(false).WithDescription("(optional) Generate a reading constructor and SQL statement output - Activate with -g true");
            parser.Setup(arg => arg.GenerateMarkupPages).As('m', "generatemarkuppages")
                .SetDefault(false)
                .WithDescription("(optional) Generate markup pages for database and tables which can be used in wikis - Activate with -m true");
            parser.Setup(arg => arg.MarkupDatabaseNameReplacement).As('r', "markupdatabasenamereplacement")
                .SetDefault("").WithDescription("(optional) Will use this instead of database name for wiki breadcrump generation");
            parser.SetupHelp("?", "help").Callback(text => Console.WriteLine(text));

            var result = parser.Parse(args);
            if (!result.HasErrors)
            {
                var conf = parser.Object as ApplicationArguments;
                if (conf.Database is null)
                {
                    Console.WriteLine("You didn't specify a database");
                    return;
                }

                var confString =
                    $"Server={conf.IP};Port={conf.Port};Uid={conf.User};Pwd={conf.Password};Database={conf.Database}";
                Console.WriteLine(confString);

                var database = new Dictionary<string, List<Column>>();
                var tableComment = new Dictionary<string, string>();

                using (var con = new MySqlConnection(confString))
                {
                    con.Open();

                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText =
                            $"SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE,COLUMN_DEFAULT,IS_NULLABLE, COLUMN_KEY ,COLUMN_COMMENT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{conf.Database}'";
                  
                        if (!conf.Table.Equals(string.Empty))
                            cmd.CommandText += $" AND TABLE_NAME = '{conf.Table}'";

                        var reader = cmd.ExecuteReader();
                        if (!reader.HasRows)
                            return;

                        while (reader.Read())
                            if (database.ContainsKey(reader.GetString(0)))
                                database[reader.GetString(0)].Add(new Column(reader));
                            else
                                database.Add(reader.GetString(0), new List<Column>() { new Column(reader) });
                    }

                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText =
                            $"SELECT TABLE_NAME,TABLE_COMMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='{conf.Database}'";

                        var reader = cmd.ExecuteReader();
                        if (!reader.HasRows)
                            return;

                        while (reader.Read())
                            if (tableComment.ContainsKey(reader.GetString(0)))
                                tableComment[reader.GetString(0)]=reader.GetString("TABLE_COMMENT");
                            else
                                tableComment.Add(reader.GetString(0), reader.GetString("TABLE_COMMENT"));
                    }

                    foreach (var table in database)
                        {
                            using (var cmd = con.CreateCommand())
                            {
                                // lul - is there a way to do this without this senseless statement?
                                cmd.CommandText = $"SELECT * FROM `{table.Key}` LIMIT 0";
                                var reader = cmd.ExecuteReader();
                                var schema = reader.GetSchemaTable();
                                foreach (var column in table.Value)
                                    column.Type = schema.Select($"ColumnName = '{column.Name}'")[0]["DataType"] as Type;
                            }
                        }

                    con.Close();
                }

                DbToClasses(conf.Database, database,tableComment, conf.GenerateConstructorAndOutput);
                if (conf.GenerateMarkupPages)
                    DbToMarkupPage(String.IsNullOrEmpty(conf.MarkupDatabaseNameReplacement) ? conf.Database : conf.MarkupDatabaseNameReplacement, database);
                Console.WriteLine("Successfully generated C# classes!");
            }
            Console.ReadLine();
        }
    }
}
