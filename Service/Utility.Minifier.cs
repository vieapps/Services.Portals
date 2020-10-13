#region Related components
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class Minifier
	{
		static bool IsDelimiter(int c) => c == '(' || c == ',' || c == '=' || c == ':' || c == '[' || c == '!' || c == '&' || c == '|' || c == '?' || c == '+' || c == '-' || c == '~' || c == '*' || c == '/' || c == '{' || c == '\n' || c == ',';

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
			var isDoubleSlashComment = false;
			var minified = "";
			using (var reader = new BinaryReader(stream))
				while (!isEOF)
				{
					var nextChar = reader.PeekChar();
					isEOF = nextChar == -1;            // check EOF
					if (isEOF)
						break;

					var isIgnore = false;
					int thisChar = reader.ReadByte();

					if (thisChar == '\t')
						thisChar = ' ';
					else if (thisChar == '\t' || thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
						isIgnore = true;

					else if (thisChar == ' ')
					{
						if (lastChar == ' ' || lastChar == '>' || lastChar == '<' || Minifier.IsDelimiter(lastChar))
							isIgnore = true;

						else
						{
							nextChar = reader.PeekChar();
							isEOF = nextChar == -1;            // check EOF
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
							isInComment = lastChar != ':' && lastChar != '"' && lastChar != '\'';
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

					if (!isIgnore)
						minified += (char)thisChar;
					lastChar = thisChar;
				}
			return minified;
		}

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyJs(byte[] data)
			=> Minifier.MinifyJs(data.ToMemoryStream());

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyJs(string data)
			=> Minifier.MinifyJs(data.ToBytes());

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
			var minified = "";
			using (var reader = new BinaryReader(stream))
				while (!isEOF)
				{
					var nextChar = reader.PeekChar();
					isEOF = nextChar == -1;            // check EOF
					if (isEOF)
						break;

					var isIgnore = false;
					int thisChar = reader.ReadByte();

					if (thisChar == '\t')
						thisChar = ' ';
					else if (thisChar == '\t' || thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
						isIgnore = true;

					else if (thisChar == ' ')
					{
						if (lastChar == ' ' || lastChar == '>' || Minifier.IsDelimiter(lastChar))
							isIgnore = true;

						else
						{
							nextChar = reader.PeekChar();
							isEOF = nextChar == -1;			// check EOF
							if (!isEOF)
								isIgnore = nextChar != '-' && (nextChar == '>' || Minifier.IsDelimiter(nextChar));
						}
					}

					else if (thisChar == '/')
					{
						nextChar = reader.PeekChar();
						if (nextChar == '*')
						{
							isInComment =  isIgnore = true;
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
						minified += (char)thisChar;
					lastChar = thisChar;
				}
			return minified;
		}

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyCss(byte[] data)
			=> Minifier.MinifyCss(data.ToMemoryStream());

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static string MinifyCss(string data)
			=> Minifier.MinifyCss(data.ToBytes());
	}
}