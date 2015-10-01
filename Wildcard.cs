using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitDepsPacker
{
	enum EMatch
	{
		MatchEquals,
		MatchRegex,
		MatchSubtree
	}

	struct WildcardItem
	{
		public EMatch Match;
		public string Mask;

		public WildcardItem(EMatch Match, string Mask)
		{
			this.Match = Match;
			this.Mask = Mask;
		}
	}

	class Wildcard
	{
		private static char[] Separators = { '/', '\\' };
		private WildcardItem[] Parts;
		private bool ExcludeMask;

		public Wildcard(string Mask)
		{
			ExcludeMask = Mask.StartsWith("!");
			Parts = ParseWildcard(Mask.Substring(ExcludeMask ? 1 : 0));
		}

		private static WildcardItem[] ParseWildcard(string Mask)
		{
			string[] Parts = Mask.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
			WildcardItem[] Result = new WildcardItem[Parts.Length];
			for (int i = 0; i < Parts.Length; ++i)
			{
				string Part = Parts[i];
				if (Part == "**")
				{
					Result[i] = new WildcardItem(EMatch.MatchSubtree, null);
				}
				else
				{
					string Escaped = Regex.Escape(Part);
					string Pattern = Escaped.Replace("\\*", ".*").Replace("\\?", ".");
					if (Pattern != Escaped)
					{
						Result[i] = new WildcardItem(EMatch.MatchRegex, Pattern);
					}
					else
					{
						Result[i] = new WildcardItem(EMatch.MatchEquals, Part);
					}
				}
			}
			return Result;
		}

		public bool Exclude
		{
			get
			{
				return ExcludeMask;
			}
		}

		public bool IsMatched(string path, bool FilePath)
		{
			return CheckMatched(path.Split(Separators, StringSplitOptions.RemoveEmptyEntries), 0, 0, FilePath);
		}

		private bool CheckMatched(string[] Items, int ItemsPos, int PartsPos, bool FilePath)
		{
			if (PartsPos >= Parts.Length)
			{
				return true;
			}
			if (ItemsPos >= Items.Length)
			{
				return !(FilePath || ExcludeMask);
			}
			switch (Parts[PartsPos].Match)
			{
				case EMatch.MatchEquals:
					if (!Parts[PartsPos].Mask.Equals(Items[ItemsPos], StringComparison.InvariantCultureIgnoreCase))
					{
						return false;
					}
					break;
				case EMatch.MatchRegex:
					if (!Regex.IsMatch(Items[ItemsPos], Parts[PartsPos].Mask, RegexOptions.IgnoreCase))
					{
						return false;
					}
					break;
				case EMatch.MatchSubtree:
					if (CheckMatched(Items, ItemsPos + 1, PartsPos, FilePath))
					{
						return true;
					}
					break;
			}
			return CheckMatched(Items, ItemsPos + 1, PartsPos + 1, FilePath);
		}
	}
}
