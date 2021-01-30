#region Related components
using System.IO;
using System.Text;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class Minifier
	{
		static bool IsDelimiter(int c) => c == '(' || c == ',' || c == '=' || c == ':' || c == '[' || c == '!' || c == '&' || c == '|' || c == '?' || c == '+' || c == '-' || c == '~' || c == '*' || c == '/' || c == '{' || c == '\n' || c == ',';

		static bool IsEOF(this int @char)
			=> @char == -1;

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

					if (thisChar == '\t')
						thisChar = ' ';
					else if (thisChar == '\t' || thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
						isIgnore = true;

					else if (thisChar == ' ' && !isInStringVariable)
					{
						if (isInLiterialString)
							isIgnore = isInLiterialExpression;
						else
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
			=> Minifier.MinifyJs(data.ToBytes(Encoding.UTF8));

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
					else if (thisChar == '\t' || thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
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
								isIgnore = nextChar != '-' && (nextChar == '>' || Minifier.IsDelimiter(nextChar));
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
			=> Minifier.MinifyCss(data.ToBytes(Encoding.UTF8));
	}
}