﻿/**
 * YES I REALIZE THIS IS AN UNHOLY CLUSTERFUCK.
 * 
 * PLEASE FEEL FREE TO TIDY UP.
 *  - N3XYPOO
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;

namespace OpenBYOND.VM
{
    public class ObjectTree
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Utils));

        // Can only be modified by the VM.
        public static readonly string[] ReservedSubtrees = new string[]{
            "/reserved",
        };


        private static readonly string[] reservedWords = new string[] { "else", "break", "return", "continue", "spawn" };

        /// <summary>
        /// All atoms, as a Dictionary.
        /// </summary>
        public Dictionary<string, Atom> Atoms = new Dictionary<string, Atom>();

        /// <summary>
        /// All atoms, as a tree.
        /// </summary>
        public Atom Tree = new Atom("");

        /// <summary>
        /// Standard library has been loaded.
        /// </summary>
        public bool LoadedStdLib = false;

        /// <summary>
        /// Values of #defines.
        /// </summary>
        Dictionary<string, BYONDValue> Defines = new Dictionary<string, BYONDValue>();

        /// <summary>
        /// Components of the path.
        /// </summary>
        List<string> cpath = new List<string>();

        /// <summary>
        /// How many path chunks to slice off when we go down an indent.
        /// </summary>
        Stack<int> popLevels = new Stack<int>();

        List<string> InProc = new List<string>();
        List<string> ignoreLevel = new List<string>();  // Block Comments
        Dictionary<string, string> defineMatchers = new Dictionary<string, string>();
        List<string> comments = new List<string>();
        Dictionary<string, string> fileLayouts = new Dictionary<string, string>();

        static Dictionary<string, string> ignoreTokens = new Dictionary<string, string>(){
            {"/*","*/"},  // /* */
            {"{\"","\"}"} // {" "}
        };

        int pindent = 0;  // Previous Indent
        int ignoreStartIndent = -1;

        public bool debugOn = true;
        public bool ignoreDebugOn = true;

        bool LeavePreprocessorDirectives = false;

        static ObjectTree()
        {
            Dictionary<string, string> nit = new Dictionary<string, string>(ignoreTokens);
            foreach (KeyValuePair<string, string> kvp in ignoreTokens)
            {
                nit[kvp.Value] = null;
            }
            ignoreTokens = nit;
        }

        public ObjectTree()
        {
            SetDefine("OPENBYOND", 1);
        }

        public void SetDefine(string name, int value)
        {
            Defines[name] = new BYONDValue<int?>(value);
        }

        // Indent debugging.
        private void debug(string filename, int line, List<string> path, string message)
        {
            log.DebugFormat("{0}:{1}: {2} - {3}", filename, line, "/".join(path), message);
        }

        private void ProcessAtom(string filename, int ln, string line, string atom, List<string> atom_path, int numtabs, string[] procArgs = null)
        {
            // Reserved words that show up on their own
            if (reservedWords.Contains(atom))
                return;

            // Other things to ignore (false positives, comments)
            if (atom.StartsWith("var/") || atom.StartsWith("//"))
                return;

            // Things part of a string or list.
            if (numtabs > 0 && atom.Trim().StartsWith("/"))
                return;

            if (debugOn)
                log.DebugFormat("{} > {}", numtabs, line.TrimEnd());

            // Global scope (no tabs)
            if (numtabs == 0)
            {
                this.cpath = atom_path;
                // Ensure cpath has a slash at the front (empty first entry)
                if (this.cpath.Count == 0)
                    this.cpath.Add("");
                else if (this.cpath[0] != "")
                    this.cpath.Insert(0, "");

                // Create new poplevel stack, add current path length to it.
                this.popLevels = new Stack<int>();
                this.popLevels.Push(this.cpath.Count);

                if (this.debugOn)
                    debug(filename, ln, this.cpath, "0 - " + string.Join("/", atom_path));

            }
            else if (numtabs > pindent) // Going up a tab level.
            {
                // Add path to cpath.
                this.cpath.AddRange(atom_path);

                // Add new poplevel with current path length.
                this.popLevels.Push(atom_path.Count);

                if (this.debugOn)
                    debug(filename, ln, this.cpath, ">");

            }
            else if (numtabs < pindent) // Going down.
            {
                if (this.debugOn)
                    log.DebugFormat("({0} - {1})={2}: {3}", this.pindent, numtabs, this.pindent - numtabs, string.Join("/", this.cpath));

                // This is complex as fuck, so bear with me.
                // For every poplevel we've lost, we need to slice off some path chunks or we fuck up our context.
                //
                // /butt
                //   ugly
                //     dirty    // /butt/ugly/dirty, 2 poplevels both with content 1 (number of things we added to path)
                //   nice       // Here, we slice off 1 path segment, and add "nice", getting /butt/nice.
                for (int i = 0; i < (this.pindent - numtabs + 1); i++)
                {
                    // Pop a poplevel out, find out how many path chunks we need to remove.
                    var popsToDo = this.popLevels.Pop();

                    if (this.debugOn)
                        log.DebugFormat(" pop {0} {1}", popsToDo, this.popLevels);
                    
                    // Now pop off the path segments.
                    for (int j = 0; j < popsToDo; j++)
                    {
                        this.cpath.RemoveAt(this.cpath.Count - 1);

                        if (this.debugOn)
                            log.DebugFormat("  pop {0}/{1}: {2}", i + 1, popsToDo, "/".join(this.cpath));
                    }
                }

                // Add new stuff.
                this.cpath.AddRange(atom_path);

                // Add new poplevel for the new stuff.
                this.popLevels.Push(atom_path.Count);

                if (this.debugOn)
                    debug(filename, ln, this.cpath, "<");

            }
            else if (numtabs == this.pindent) // Same level.
            {
                // Same as above, but we're only going down one indent.
                var levelsToPop = this.popLevels.Pop();

                // Pop off path segments.
                for (int i = 0; i < levelsToPop; i++)
                    this.cpath.RemoveAt(this.cpath.Count - 1);

                // New stuff
                this.cpath.AddRange(atom_path);

                // New poplevel.
                this.popLevels.Push(atom_path.Count);

                if (this.debugOn)
                    log.DebugFormat("popLevels: {0}", this.popLevels.Count);
                if (this.debugOn)
                    debug(filename, ln, this.cpath, ">");
            }

            var origpath = string.Join("/", this.cpath);

            // definition?
            List<string> defs = new List<string>();

            // Trim off /proc or /var, if needed.
            List<string> prep_path = new List<string>(this.cpath);

            foreach (string special in new string[] { "proc" })
            {
                if (prep_path.Contains(special))
                {
                    defs.Add(special);
                    prep_path.Remove(special);
                }
            }

            var npath = "/".join(prep_path);

            if (!Atoms.ContainsKey(npath))
            {
                if (procArgs != null)
                {
                    //assert npath.endswith(')')
                    // if origpath != npath:
                    //    print(origpath,proc_def)
                    return; // TODO: Fix this.
                    /*
                    proc = Proc(npath, procArgs, filename, ln)
                    proc.origpath = origpath
                    proc.definition = 'proc' in defs
                    this.Atoms[npath] = proc
                    */
                }
                else
                {
                    this.Atoms[npath] = new Atom(npath, filename, ln);
                }
                // if this.debugOn: print('Added ' + npath)
            }
            this.pindent = numtabs;
            return;//this.Atoms[npath];
        }

        private string[] SplitPath(string path)
        {
            var o = new List<string>();
            var buf = new List<string>();
            bool inProc = false;
            foreach (string chunk in path.Split('/'))
            {
                if (!inProc)
                {
                    if (chunk.Contains('(') && !chunk.Contains(')'))
                    {
                        inProc = true;
                        buf.Add(chunk);
                    }
                    else
                    {
                        o.Add(chunk);
                    }
                }
                else
                {
                    if (chunk.Contains(')'))
                    {
                        buf.Add(chunk);
                        o.Add(string.Join("/", buf));
                        inProc = true;
                    }
                    else
                    {
                        buf.Add(chunk);
                    }
                }
            }
            return o.ToArray();
        }
    }
}
