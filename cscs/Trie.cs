using System;
using System.Collections.Generic;

namespace SplitAndMerge
{
    public class WordHint
    {
        string m_text;

        public int Id { get; }
        public string OriginalText { get; }
        public string Text { get { return m_text; } }

        public bool Equals(WordHint other)
        {
            return Id == other.Id;
        }
        public bool Exists(List<WordHint> others)
        {
            for (int i = 0; i < others.Count; i++)
            {
                if (Equals(others[i]))
                {
                    return true;
                }
            }
            return false;
        }

        public WordHint(string word, int id)
        {
            OriginalText = word;
            Id = id;
            m_text = Utils.RemovePrefix(OriginalText);
        }
    }
    public class TrieCell
    {
        string m_name;
        WordHint m_wordHint;

        Dictionary<string, TrieCell> m_children = new Dictionary<string, TrieCell>();

        public int Level { get; set; }
        public WordHint WordHint { get { return m_wordHint; } }
        public Dictionary<string, TrieCell> Children { get { return m_children; } }

        public TrieCell(string name = "", WordHint wordHint = null, int level = 0)
        {
            if (wordHint != null && wordHint.Text == name)
            {
                m_wordHint = wordHint;
            }

            m_name = name;
            Level = level;
        }

        public bool AddChild(WordHint wordHint)
        {
            if (!string.IsNullOrEmpty(m_name) &&
                !wordHint.Text.StartsWith(m_name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int newLevel = Level + 1;

            bool lastChild = newLevel >= wordHint.Text.Length;

            string newName = lastChild ? wordHint.Text :
                                         wordHint.Text.Substring(0, newLevel);

            TrieCell oldChild = null;
            if (m_children.TryGetValue(newName, out oldChild))
            {
                return oldChild.AddChild(wordHint);
            }

            TrieCell newChild = new TrieCell(newName, wordHint, newLevel);
            m_children[newName] = newChild;

            if (newLevel < wordHint.Text.Length)
            { // if there are still chars left, add a grandchild recursively.
                newChild.AddChild(wordHint);
            }

            return true;
        }
    }

    public class Trie : Variable
    {
        TrieCell m_root;

        /*public override Variable Clone()
        {
            Trie newVar = new Trie();
            newVar.Copy(this);
            newVar.m_root = m_root;
            return newVar;
        }*/
        Trie()
        {
        }
        public Trie(List<string> words)
        {
            m_root = new TrieCell();

            int index = 0;
            foreach (string word in words)
            {
                AddWord(word, index++);
            }
        }

        void AddWord(string word, int index)
        {
            WordHint hint = new WordHint(word, index);
            m_root.AddChild(hint);

            string text = hint.Text;
            int space = text.IndexOf(' ');
            while (space > 0)
            {
                string candidate = text.Substring(space + 1);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    hint = new WordHint(candidate, index);
                    m_root.AddChild(hint);
                    //Console.WriteLine("TRIE candidate [{0}] voice {1}", candidate, m_voice);
                }
                if (text.Length < space + 1)
                {
                    break;
                }
                space = text.IndexOf(' ', space + 1);
            }
        }

        public void Search(string text, int max, List<WordHint> results)
        {
            text = Utils.RemovePrefix(text);
            TrieCell current = m_root;

            for (int level = 1; level <= text.Length && current != null; level++)
            {
                string substr = text.Substring(0, level);
                if (!current.Children.TryGetValue(substr, out current))
                {
                    current = null;
                }
            }

            if (current == null)
            {
                return; // passed text doesn't exist
            }

            AddAll(current, max, results);
        }

        void AddAll(TrieCell cell, int max, List<WordHint> results)
        {
            if (cell.WordHint != null && !cell.WordHint.Exists(results))
            {
                results.Add(cell.WordHint);
            }
            if (results.Count >= max)
            {
                return;
            }

            foreach (var entry in cell.Children)
            {
                TrieCell child = entry.Value;
                AddAll(child, max, results);

                if (results.Count >= max)
                {
                    return;
                }
            }
        }
    }
}

