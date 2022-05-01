#region Related components
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class Minifier
	{
		static bool IsDelimiter(int c) => c == '(' || c == ',' || c == '=' || c == ':' || c == '[' || c == '!' || c == '&' || c == '|' || c == '?' || c == '+' || c == '-' || c == '~' || c == '*' || c == '/' || c == '{' || c == '\n' || c == ',';

		static bool IsEOF(this int @char)
			=> @char == -1;

		static readonly int BlockSize = 16384;

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="stream"></param>
		/// <returns></returns>
		public static string MinifyJs(Stream stream)
		{
			var lastChar = 1;
			var isEOF = false;
			var isInComment = false;
			var isInStringVariable = false;
			var isInLiterialString = false;
			var isInLiterialExpression = false;
			var isDoubleSlashComment = false;
			var decoder = Encoding.UTF8.GetDecoder();
			var buffer = new char[1];
			var minified = "";
			using (var reader = new BinaryReader(stream, Encoding.UTF8))
				while (!isEOF)
				{
					var nextChar = reader.PeekChar();
					isEOF = nextChar.IsEOF();
					if (isEOF)
						break;

					var isIgnore = false;
					var thisChar = (int)reader.ReadByte();

					if (thisChar == '\t' && !isInLiterialString)
						thisChar = ' ';

					if (thisChar == '\t' || thisChar == '\n' || thisChar == '\r')
						isIgnore = true;

					else if (thisChar == ' ' && !isInStringVariable && (!isInLiterialString || isInLiterialExpression))
					{
						if (lastChar == ' ' || lastChar == '>' || lastChar == '<' || Minifier.IsDelimiter(lastChar))
							isIgnore = true;
						else
						{
							nextChar = reader.PeekChar();
							isEOF = nextChar.IsEOF();
							if (!isEOF)
								isIgnore = nextChar == '>' || nextChar == '<' || nextChar == '}' || nextChar == ')' || Minifier.IsDelimiter(nextChar);
						}
					}

					else if (thisChar == '/')
					{
						nextChar = reader.PeekChar();
						if (nextChar == '*')
						{
							isInComment = isIgnore = true;
							isDoubleSlashComment = false;
						}
						else if (nextChar == '/')
						{
							isInComment = lastChar != ':' && lastChar != '"' && lastChar != '\'' && lastChar != '\\';
							isIgnore = isInComment;
							isDoubleSlashComment = isInComment && nextChar == '/';
						}
					}

					// ignore all characters till we reach end of comment
					if (isInComment)
					{
						isInLiterialString = isInLiterialExpression = isInStringVariable = false;
						isIgnore = true;
						while (true)
						{
							thisChar = reader.ReadByte();
							if (thisChar == '*')
							{
								nextChar = reader.PeekChar();
								if (nextChar == '/')
								{
									thisChar = reader.ReadByte();
									isInComment = false;
									break;
								}
							}
							if (isDoubleSlashComment && thisChar == '\n')
							{
								isInComment = false;
								break;
							}
						}
					}

					// special characters (string)
					else if (!isIgnore)
					{
						if (thisChar == '`')
						{
							isInLiterialString = !isInLiterialString;
							isInLiterialExpression = isInStringVariable = false;
						}
						else if (thisChar == '{' && lastChar == '$' && isInLiterialString)
						{
							isInLiterialExpression = true;
							isInStringVariable = false;
						}
						else if (thisChar == '}' && isInLiterialString && isInLiterialExpression)
						{
							isInLiterialExpression = isInStringVariable = false;
						}
						else if ((thisChar == '\'' || thisChar == '"') && lastChar != '\\')
						{
							isInStringVariable = isInLiterialString
								? isInLiterialExpression && !isInStringVariable
								: !isInStringVariable;
						}
					}

					if (!isIgnore)
						minified += decoder.GetChars(new[] { (byte)thisChar }, 0, 1, buffer, 0) > 0 ? $"{buffer[0]}" : "";
					lastChar = thisChar;
				}
			return minified.Replace("; }", ";}");
		}

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyJs(byte[] data)
		{
			using (var stream = data.ToMemoryStream())
				return Minifier.MinifyJs(stream);
		}

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyJs(string data)
		{
			var blocks = new List<string>();
			while (data.Length > 0)
			{
				var block = data.Length > Minifier.BlockSize ? data.Substring(0, Minifier.BlockSize) : data;
				data = data.Remove(0, block.Length);
				if (data.Length > 0)
					while (block.Last() != '}' && data.Length > 0)
					{
						block += data.First();
						data = data.Remove(0, 1);
					}
				blocks.Add(block);
			}
			return blocks.Select(block => Minifier.MinifyJs(block.ToBytes(Encoding.UTF8))).Join("");
		}

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="stream"></param>
		/// <returns></returns>
		public static string MinifyCss(Stream stream)
		{
			var lastChar = 1;
			var isEOF = false;
			var isInComment = false;
			var isDoubleSlashComment = false;
			var decoder = Encoding.UTF8.GetDecoder();
			var buffer = new char[1];
			var minified = "";
			using (var reader = new BinaryReader(stream, Encoding.UTF8))
				while (!isEOF)
				{
					var nextChar = reader.PeekChar();
					isEOF = nextChar.IsEOF();
					if (isEOF)
						break;

					var isIgnore = false;
					int thisChar = reader.ReadByte();

					if (thisChar == '\t')
						thisChar = ' ';

					if (thisChar == '\n' || thisChar == '\r')
						isIgnore = true;

					else if (thisChar == ' ')
					{
						if (lastChar == '-')
							isIgnore = false;

						else if (lastChar == ' ' || lastChar == '>' || lastChar == '}' || Minifier.IsDelimiter(lastChar))
							isIgnore = true;

						else
						{
							nextChar = reader.PeekChar();
							isEOF = nextChar.IsEOF();
							if (!isEOF)
								isIgnore = nextChar != '-' && nextChar != '[' && (nextChar == '>' || Minifier.IsDelimiter(nextChar));
						}
					}

					else if (thisChar == '/')
					{
						nextChar = reader.PeekChar();
						if (nextChar == '*')
						{
							isInComment = isIgnore = true;
							isDoubleSlashComment = false;
						}
						else if (nextChar == '/')
						{
							isInComment = lastChar != ':';
							isIgnore = isInComment;
							isDoubleSlashComment = isInComment && nextChar == '/';
						}
					}

					// ignore all characters till we reach end of comment
					if (isInComment)
					{
						isIgnore = true;
						while (true)
						{
							thisChar = reader.ReadByte();
							if (thisChar == '*')
							{
								nextChar = reader.PeekChar();
								if (nextChar == '/')
								{
									thisChar = reader.ReadByte();
									isInComment = false;
									break;
								}
							}
							if (isDoubleSlashComment && thisChar == '\n')
							{
								isInComment = false;
								break;
							}
						}
					}

					// update the valid data
					if (!isIgnore)
						minified += decoder.GetChars(new[] { (byte)thisChar }, 0, 1, buffer, 0) > 0 ? $"{buffer[0]}" : "";
					lastChar = thisChar;
				}
			return minified.Replace("; }", ";}");
		}

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyCss(byte[] data)
		{
			using (var stream = data.ToMemoryStream())
				return Minifier.MinifyCss(stream);
		}

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyCss(string data)
		{
			var blocks = new List<string>();
			while (data.Length > 0)
			{
				var block = data.Length > Minifier.BlockSize ? data.Substring(0, Minifier.BlockSize) : data;
				data = data.Remove(0, block.Length);
				if (data.Length > 0)
					while (block.Last() != '}' && data.Length > 0)
					{
						block += data.First();
						data = data.Remove(0, 1);
					}
				blocks.Add(block);
			}
			return blocks.Select(block => Minifier.MinifyCss(block.ToBytes(Encoding.UTF8))).Join("");
		}
	}
}