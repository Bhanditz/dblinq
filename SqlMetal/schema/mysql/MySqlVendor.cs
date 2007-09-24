using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using MySql.Data.MySqlClient;
using SqlMetal.util;

namespace SqlMetal.schema.mysql
{
    /// <summary>
    /// this class contains MySql-specific way of retrieving DB schema.
    /// </summary>
    class MySqlVendor : IDBVendor
    {
        public string VendorName(){ return "MySql"; }

        /// <summary>
        /// main entry point to load schema
        /// </summary>
        public DlinqSchema.Database LoadSchema()
        {
            string connStr = string.Format("server={0};user id={1}; password={2}; database={3}; pooling=false"
                , mmConfig.server, mmConfig.user, mmConfig.password, mmConfig.database);

            MySqlConnection conn = new MySqlConnection(connStr);
            conn.Open();

            DlinqSchema.Database schema = new DlinqSchema.Database();
            schema.Name = mmConfig.database;
            schema.Class = FormatTableName(schema.Name);
            //DlinqSchema.Schema schema0 = new DlinqSchema.Schema();
            //schema0.Name = "Default";
            //schema.Schemas.Add( schema0 );

            //##################################################################
            //step 1 - load tables
            TableSql tsql = new TableSql();
            List<TableRow> tables = tsql.getTables(conn,mmConfig.database);
            if(tables==null || tables.Count==0){
                Console.WriteLine("No tables found for schema "+mmConfig.database+", exiting");
                return null;
            }

            foreach(TableRow tblRow in tables)
            {
                DlinqSchema.Table tblSchema = new DlinqSchema.Table();
                tblSchema.Name = tblRow.table_name;
                tblSchema.Class = FormatTableName(tblRow.table_name);
                schema.Tables.Add( tblSchema );
            }

            //ensure all table schemas contain one type:
            //foreach(DlinqSchema.Table tblSchema in schema0.Tables)
            //{
            //    tblSchema.Types.Add( new DlinqSchema.Type());
            //}

            //##################################################################
            //step 2 - load columns
            ColumnSql csql = new ColumnSql();
            List<Column> columns = csql.getColumns(conn,mmConfig.database);
            
            foreach(Column columnRow in columns)
            {
                //find which table this column belongs to
                DlinqSchema.Table tableSchema = schema.Tables.FirstOrDefault(tblSchema => columnRow.table_name==tblSchema.Name);
                if(tableSchema==null)
                {
                    Console.WriteLine("ERROR L46: Table '"+columnRow.table_name+"' not found for column "+columnRow.column_name);
                    continue;
                }
                DlinqSchema.Column colSchema = new DlinqSchema.Column();
                colSchema.Name = columnRow.column_name;
                colSchema.DbType = columnRow.datatype; //.column_type ?
                colSchema.IsIdentity = columnRow.column_key=="PRI";
                colSchema.IsAutogen = columnRow.extra=="auto_increment";
                //colSchema.IsVersion = ???
                colSchema.Nullable = columnRow.isNullable;
                colSchema.Type = Mappings.mapSqlTypeToCsType(columnRow.datatype, columnRow.column_type);
                
                //this will be the c# field name
                colSchema.Property = CSharp.IsCsharpKeyword(columnRow.column_name) 
                    ? columnRow.column_name+"_" //avoid keyword conflict - append underscore
                    : columnRow.column_name;

                colSchema.Type = Mappings.mapSqlTypeToCsType(columnRow.datatype, columnRow.column_type);
                if(CSharp.IsValueType(colSchema.Type) && columnRow.isNullable)
                colSchema.Type += "?";

                //tableSchema.Types[0].Columns.Add(colSchema);
                tableSchema.Columns.Add(colSchema);
            }

            //##################################################################
            //step 3 - load foreign keys etc
            KeyColumnUsageSql ksql = new KeyColumnUsageSql();
            List<KeyColumnUsage> constraints = ksql.getConstraints(conn,mmConfig.database);

            TableSorter.Sort(tables, constraints); //sort tables - parents first

            foreach(KeyColumnUsage keyColRow in constraints)
            {
                //find my table:
                DlinqSchema.Table table = schema.Tables.FirstOrDefault(t => keyColRow.table_name==t.Name);
                if(table==null)
                {
                    Console.WriteLine("ERROR L46: Table '"+keyColRow.table_name+"' not found for column "+keyColRow.column_name);
                    continue;
                }

                if(keyColRow.constraint_name=="PRIMARY")
                {
                    //A) add primary key
                    DlinqSchema.ColumnSpecifier primaryKey = new DlinqSchema.ColumnSpecifier();
                    primaryKey.Name = keyColRow.constraint_name;
                    DlinqSchema.ColumnName colName = new DlinqSchema.ColumnName();
                    colName.Name = keyColRow.column_name;
                    primaryKey.Columns.Add(colName);
                    table.PrimaryKey.Add( primaryKey );

                    //B mark the column itself as being 'IsIdentity=true'
                    //DlinqSchema.Column col = table.Types[0].Columns.FirstOrDefault(c => keyColRow.column_name==c.Name);
                    //if(col!=null)
                    //{
                    //    col.IsIdentity = true;
                    //}
                } else 
                {
                    //if not PRIMARY, it's a foreign key.
                    //both parent and child table get an [Association]
                    //table.as
                    DlinqSchema.Association assoc = new DlinqSchema.Association();

                    //assoc.Kind = DlinqSchema.RelationshipKind.ManyToOneChild;
                    assoc.Kind = DlinqSchema.RelationshipKind.ManyToOneParent;

                    assoc.Name = keyColRow.constraint_name;
                    //assoc.Target = keyColRow.referenced_table_name + "."+ keyColRow.referenced_column_name;
                    assoc.Target = keyColRow.referenced_table_name;
                    DlinqSchema.ColumnName assocCol = new DlinqSchema.ColumnName();
                    assocCol.Name = keyColRow.column_name;
                    assoc.Columns.Add(assocCol);
                    //table.Types[0].Associations.Add(assoc);
                    table.Associations.Add(assoc);

                    //and insert the reverse association:
                    DlinqSchema.Association assoc2 = new DlinqSchema.Association();

                    //assoc2.Kind = DlinqSchema.RelationshipKind.ManyToOneParent;
                    assoc2.Kind = DlinqSchema.RelationshipKind.ManyToOneChild;

                    assoc2.Name = keyColRow.constraint_name;
                    assoc2.Target = keyColRow.table_name;
                    DlinqSchema.ColumnName assocCol2 = new DlinqSchema.ColumnName();
                    assocCol2.Name = keyColRow.column_name;
                    assoc2.Columns.Add(assocCol2);
                    DlinqSchema.Table parentTable = schema.Tables.FirstOrDefault(t => keyColRow.referenced_table_name==t.Name);
                    if (parentTable == null)
                    {
                        Console.WriteLine("ERROR 148: parent table not found: " + keyColRow.referenced_table_name);
                    }
                    else
                    {
                        //parentTable.Types[0].Associations.Add(assoc2);
                        parentTable.Associations.Add(assoc2);
                    }

                }

            }

            return schema;
        }

        public static string FormatTableName(string table_name)
        {
            //TODO: allow custom renames via config file - 
            //- this could solve keyword conflict etc
            string name1 = table_name;
            string name2 = mmConfig.forceUcaseTableName
                ? name1.Capitalize() //Char.ToUpper(name1[0])+name1.Substring(1)
                : name1;

            //heuristic to convert 'Products' table to class 'Product'
            //TODO: allow customized tableName-className mappings from an XML file
            name2 = name2.Singularize();

            return name2;
        }

    }
}