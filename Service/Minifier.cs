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
	public class Minifier
	{
		bool IsDelimiter(int c) => c == '(' || c == ',' || c == '=' || c == ':' || c == '[' || c == '!' || c == '&' || c == '|' || c == '?' || c == '+' || c == '-' || c == '~' || c == '*' || c == '/' || c == '{' || c == '\n' || c == ',';

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="stream"></param>
		/// <returns></returns>
		public string MinifyJs(Stream stream)
		{
			// current byte read
			var lastChar = 1;
			// previous byte read
			int thisChar;
			// byte read in peek()
			int nextChar;
			// loop control
			var endProcess = false;
			// true when current bytes are part of a comment
			var inComment = false;
			// '//' comment
			var isDoubleSlashComment = false;
			// minified data
			var minified = "";
			using (var reader = new BinaryReader(stream))
				while (!endProcess)
				{
					// check for EOF before reading
					endProcess = reader.PeekChar() == -1;
					if (endProcess)
						break;

					var ignore = false;
					thisChar = reader.ReadByte();

					if (thisChar == '\t')
						thisChar = ' ';
					else if (thisChar == '\t')
						thisChar = '\n';
					else if (thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
						ignore = true;

					else if (thisChar == ' ')
					{
						if (lastChar == ' ' || lastChar == '>' || lastChar == '<' || this.IsDelimiter(lastChar))
							ignore = true;
						else
						{
							// check for EOF
							endProcess = reader.PeekChar() == -1;
							if (!endProcess)
							{
								nextChar = reader.PeekChar();
								if (nextChar == '>' || nextChar == '<' || nextChar == '}' || nextChar == ')' || this.IsDelimiter(nextChar))
									ignore = true;
							}
						}
					}

					else if (thisChar == '/')
					{
						nextChar = reader.PeekChar();
						if (nextChar == '*')
						{
							ignore = true;
							inComment = true;
							isDoubleSlashComment = false;
						}
						else if (nextChar == '/')
						{
							ignore = lastChar != ':';
							inComment = lastChar != ':';
							isDoubleSlashComment = inComment && nextChar == '/';
						}
					}

					// ignore all characters till we reach end of comment
					if (inComment)
					{
						while (true)
						{
							thisChar = reader.ReadByte();
							if (thisChar == '*')
							{
								nextChar = reader.PeekChar();
								if (nextChar == '/')
								{
									thisChar = reader.ReadByte();
									inComment = false;
									break;
								}
							}
							if (isDoubleSlashComment && thisChar == '\n')
							{
								inComment = false;
								break;
							}
						}
						ignore = true;
					}

					if (!ignore)
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
		public string MinifyJs(byte[] data)
			=> this.MinifyJs(data.ToMemoryStream());

		/// <summary>
		/// Minifies Javascript code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public string MinifyJs(string data)
			=> this.MinifyJs(data.ToBytes());

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="stream"></param>
		/// <returns></returns>
		public string MinifyCss(Stream stream)
		{
			// current byte read
			var lastChar = 1;
			// previous byte read
			int thisChar;
			// byte read in peek()
			int nextChar;
			// loop control
			var endProcess = false;
			// true when current bytes are part of a comment
			var inComment = false;
			// '//' comment
			var isDoubleSlashComment = false;
			// minified data
			var minified = "";
			using (var reader = new BinaryReader(stream))
				while (!endProcess)
				{
					// check for EOF before reading
					endProcess = reader.PeekChar() == -1;
					if (endProcess)
						break;

					var ignore = false;
					thisChar = reader.ReadByte();

					if (thisChar == '\t')
						thisChar = ' ';
					else if (thisChar == '\t')
						thisChar = '\n';
					else if (thisChar == '\r')
						thisChar = '\n';

					if (thisChar == '\n')
						ignore = true;

					else if (thisChar == ' ')
					{
						if (lastChar == ' ' || lastChar == '>' || this.IsDelimiter(lastChar))
							ignore = true;
						else
						{
							// check for EOF
							endProcess = reader.PeekChar() == -1;
							if (!endProcess)
							{
								nextChar = reader.PeekChar();
								if (nextChar == '>' || this.IsDelimiter(nextChar))
									ignore = true;
							}
						}
					}

					else if (thisChar == '/')
					{
						nextChar = reader.PeekChar();
						if (nextChar == '*')
						{
							ignore = true;
							inComment = true;
							isDoubleSlashComment = false;
						}
						else if (nextChar == '/')
						{
							ignore = lastChar != ':';
							inComment = lastChar != ':';
							isDoubleSlashComment = inComment && nextChar == '/';
						}
					}

					// ignore all characters till we reach end of comment
					if (inComment)
					{
						while (true)
						{
							thisChar = reader.ReadByte();
							if (thisChar == '*')
							{
								nextChar = reader.PeekChar();
								if (nextChar == '/')
								{
									thisChar = reader.ReadByte();
									inComment = false;
									break;
								}
							}
							if (isDoubleSlashComment && thisChar == '\n')
							{
								inComment = false;
								break;
							}
						}
						ignore = true;
					}

					if (!ignore)
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
		public string MinifyCss(byte[] data)
			=> this.MinifyCss(data.ToMemoryStream());

		/// <summary>
		/// Minifies CSS code
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public string MinifyCss(string data)
			=> this.MinifyCss(data.ToBytes());
	}
}