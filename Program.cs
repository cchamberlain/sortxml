using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace sortxml {

    internal class Program {
        private static bool sort_node = true,
                            sort_attr = true,
                            pretty = true,
                            pause,
                            overwriteSelf;
        private static StringComparison
            sort_node_comp = StringComparison.CurrentCulture, // Default to case-sensitive sorting.
            sort_attr_comp = StringComparison.CurrentCulture;

        private static string primary_attr = "";

        private static int Main(string[] arguments) {
            string inf = "",
                   outf = "";

            var doc = new XmlDocument();
            foreach (var arg in arguments) {
                var a = arg;
                if (a[0] == '-' || a[0] == '/' || a[0] == '!') {
                    while (a[0] == '-' || a[0] == '/') a = a.Substring(1);
                    var al = a.ToLower();

                    if (al.Equals("?") || al.Equals("help")) {
                        usage();
                        return 0;
                    }
                    if (al.Equals("p") || al.Equals("pause")) pause = true;
                    else if (al.Equals("i") || al.StartsWith("casei") || al.StartsWith("case-i")) {
                        sort_node_comp = StringComparison.CurrentCultureIgnoreCase;
                        sort_attr_comp = StringComparison.CurrentCultureIgnoreCase;
                    } else if (al.Equals("!i") || al.StartsWith("cases") || al.StartsWith("case-s")) {
                        sort_node_comp = StringComparison.CurrentCulture;
                        sort_attr_comp = StringComparison.CurrentCulture;
                    } else if (al.Equals("s") || al.Equals("sort") || al.StartsWith("sortall") || al.StartsWith("sort-all")) {
                        sort_node = true;
                        sort_attr = true;
                    } else if (al.Equals("!s") || al.Equals("!sort") || al.StartsWith("!sortall") || al.StartsWith("!sort-all")) {
                        sort_node = false;
                        sort_attr = false;
                    } else if (al.StartsWith("sortn") || al.StartsWith("sort-n")) sort_node = true;
                    else if (al.StartsWith("!sortn") || al.StartsWith("!sort-n")) sort_node = false;
                    else if (al.StartsWith("sorta") || al.StartsWith("sort-a")) sort_attr = true;
                    else if (al.StartsWith("!sorta") || al.StartsWith("!sort-a")) sort_attr = false;
                    else if (al.StartsWith("pretty") || al.StartsWith("!pretty")) pretty = al.StartsWith("pretty");
                    else if (al.StartsWith("overwrite") || al.StartsWith("!overwrite")) overwriteSelf = al.StartsWith("overwrite");
                    else if (al.StartsWith("primary:")) primary_attr = al.Substring("primary:".Length);
                } else {
                    if (inf.Length == 0) inf = a;
                    else if (outf.Length == 0) outf = a;
                    else Console.WriteLine("**** Unknown command: " + a);
                }
            }

            if (inf.Length == 0) {
                usage();
                return 1;
            }

            try {
                doc.LoadXml(File.ReadAllText(inf));
                doc.PreserveWhitespace = !pretty;
            } catch (Exception ex) {
                Console.WriteLine("**** Could not load input file");
                Console.WriteLine(ex.Message);
                return 100;
            }

            if (sort_attr) {
                if (string.IsNullOrEmpty(primary_attr))
                    primary_attr = "GUID";
                SortNodeAttrs(doc.DocumentElement);
            }
            if (sort_node) SortNodes(doc.DocumentElement);

            if (outf.Length == 0 && overwriteSelf) outf = inf;

            if (outf.Length > 0) {
                try {
                    doc.Save(outf);
                } catch (Exception ex) {
                    Console.WriteLine("**** Could not save output file");
                    Console.WriteLine(ex.Message);
                    return 101;
                }
            } else doc.Save(Console.Out);

            if (!pause) return 0;

            Console.Write("Press any key to quit: ");
            Console.ReadKey(true);
            Console.WriteLine();
            return 0;
        }

        private static void SortNodes(XmlNode node) {
            // Go down to the furthest child and start there..
            // That is so I can include child nodes in the current node's sort,
            // if all of it's attributes match..
            for (int i = 0, len = node.ChildNodes.Count; i < len; i++)
                SortNodes(node.ChildNodes[i]);

            // Remove, sort, then re-add the node's children.
            if (!sort_node || node.ChildNodes.Count <= 0) return;
            var nodes = new List<XmlNode>(node.ChildNodes.Count);

            for (var i = node.ChildNodes.Count - 1; i >= 0; i--) {
                nodes.Add(node.ChildNodes[i]);
                node.RemoveChild(node.ChildNodes[i]);
            }

            nodes.Sort(SortDelegate);

            foreach (var t in nodes)
                node.AppendChild(t);
        }

        private static int SortDelegate(XmlNode a, XmlNode b) {
            var result = string.Compare(a.Name, b.Name, sort_node_comp);

            // NOTE: Always sort the _nodes_ based on its attributes (when the 
            //       name matches), but don't actually sort the node's attributes.
            //       (Sorting attributes is done before node sorting happens,
            //       if specified).
            if (result != 0 || a.Attributes == null || b.Attributes == null) return result;
            var col1 = (a.Attributes.Count >= b.Attributes.Count) ? a.Attributes : b.Attributes;
            var col2 = (a.Attributes.Count >= b.Attributes.Count) ? b.Attributes : a.Attributes;

            for (var i = 0; i < col1.Count; i++) {
                if (i < col2.Count) {
                    var aa = col1[i];
                    var bb = col2[i];
                    result = string.Compare(aa.Name, bb.Name, sort_attr_comp);
                    if (result == 0) {
                        result = string.Compare(aa.Value, bb.Value, sort_attr_comp);
                        if (result != 0)
                            return result;
                        // Attribute name and value match.. continue loop.
                    } else return result;
                } else return 1;
            }

            // If we get here, that means that the node's attributes (and values) all match..
            // TODO: Should we go down into the child node collections for sorting?
            //       See example `c.xml`..
            //Console.WriteLine(a.Name + "==" + b.Name + " all attributes matched");

            return result;
        }

        private static void SortNodeAttrs(XmlNode node) {
            // Remove, sort, then re-add the node's attributes.
            if (sort_attr && node.Attributes != null && node.Attributes.Count > 0)
                SortXmlAttributeCollection(node.Attributes);

            // Sort the children node's attributes also.
            for (int i = 0, len = node.ChildNodes.Count; i < len; i++) SortNodeAttrs(node.ChildNodes[i]);
        }

        private static void SortXmlAttributeCollection(XmlAttributeCollection col) {
            // Remove, sort, then re-add the attributes to the collection.
            if (!sort_attr || col == null || col.Count <= 0) return;
            var attrs = new List<XmlAttribute>(col.Count);
            for (var i = col.Count - 1; i >= 0; i--) {
                attrs.Add(col[i]);
                col.RemoveAt(i);
            }

            SortAttributeList(attrs);
            foreach (var t in attrs) col.Append(t);
        }

        private static void SortAttributeList(List<XmlAttribute> attrs) {
            int result;
            attrs.Sort((a, b) => {
                           result = string.Compare(a.Name, b.Name, sort_attr_comp);
                           if (result == 0)
                               return string.Compare(a.Value, b.Value, sort_attr_comp);
                           if (primary_attr.Length <= 0) return result;
                           // If a primary_attr is specified, it is always made the first attribute!
                           if (a.Name.Equals(primary_attr, sort_attr_comp))
                               return -1;
                           return b.Name.Equals(primary_attr, sort_attr_comp) ? 1 : result;
                       });
        }

        private static void usage() => Console.Write(GetEmbeddedReadme());

        public static string GetEmbeddedReadme() {
            var asm = Assembly.GetExecutingAssembly();
            var strm = asm.GetManifestResourceStream("sortxml.README.md");

            if (strm == null)
                return string.Empty;

            string result;
            using (var reader = new StreamReader(strm)) {
                result = reader.ReadToEnd();
                reader.Close();
            }

            // clean it up just a tiny bit..
            var ar = new List<string>(result.Trim().Split('\n'));
            ar.RemoveRange(0, 3);

            for (var i = 0; i < ar.Count; i++) {
                if (ar[i].StartsWith("    "))
                    ar[i] = ar[i].Substring(2);
            }
            ar.Add("");
            return string.Join(Environment.NewLine, ar);
        }
    }

}
