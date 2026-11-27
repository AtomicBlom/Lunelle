using System.Globalization;
using System.Text;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>Reads a music file's JSON into <see cref="JNode"/>s, comments and trailing commas allowed.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class JReader
{
	/// <summary>Parses <paramref name="text"/>.</summary>
	/// <exception cref="FormatError">It isn't JSON; the error says where.</exception>
	public static JNode Parse(string text) => new Reader(text).Document();

	private sealed class Reader
	{
		private readonly string _text;
		private int _at;

		public Reader(string text) => _text = text;

		public JNode Document()
		{
			// A byte-order mark is not part of the JSON
			if (_text.Length > 0 && _text[0] == '﻿')
				_at = 1;
			var value = Value();
			Skip();
			if (_at < _text.Length)
				throw Error("something follows the value");
			return value;
		}

		private FormatError Error(string why) => new(why, Math.Min(_at, _text.Length), 0);

		private void Skip()
		{
			while (_at < _text.Length)
			{
				if (char.IsWhiteSpace(_text[_at]))
					_at++;
				else if (_text[_at] == '/' && _at + 1 < _text.Length && _text[_at + 1] == '/')
					while (_at < _text.Length && _text[_at] != '\n')
						_at++;
				else if (_text[_at] == '/' && _at + 1 < _text.Length && _text[_at + 1] == '*')
				{
					var end = _text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
					_at = end < 0 ? _text.Length : end + 2;
				}
				else
					return;
			}
		}

		private JNode Value()
		{
			Skip();
			if (_at >= _text.Length)
				throw Error("it ends where a value should be");
			var start = _at;
			switch (_text[_at])
			{
				case '{':
					return Object();
				case '[':
					return Array();
				case '"':
					var value = String();
					return new JString(value) { Start = start, Length = _at - start };
				case 't' when Word("true"):
					return new JBool(true) { Start = start, Length = 4 };
				case 'f' when Word("false"):
					return new JBool(false) { Start = start, Length = 5 };
				case 'n' when Word("null"):
					return new JNull { Start = start, Length = 4 };
				default:
					while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] is '-' or '+' or '.' or 'e' or 'E'))
						_at++;
					var number = _text.Substring(start, _at - start);
					if (number.Length == 0 || !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
					{
						_at = start;
						throw Error("this isn't a value");
					}
					return new JNumber(number) { Start = start, Length = number.Length };
			}
		}

		private bool Word(string word)
		{
			if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
				return false;
			_at += word.Length;
			return true;
		}

		private JObject Object()
		{
			var obj = new JObject { Start = _at++ };
			while (true)
			{
				Skip();
				if (_at < _text.Length && _text[_at] == '}')
					break;
				if (_at >= _text.Length || _text[_at] != '"')
					throw Error("a name in quotes should be here");
				var keyStart = _at;
				var key = String();
				Skip();
				if (_at >= _text.Length || _text[_at] != ':')
					throw Error("a ':' should follow the name");
				_at++;
				if (obj.Get(key) is not null)
				{
					_at = keyStart;
					throw Error($"\"{key}\" is written twice");
				}
				obj.Members.Add(new JMember(key, Value()) { KeyStart = keyStart });
				Skip();
				if (_at < _text.Length && _text[_at] == ',')
				{
					_at++;
					continue;
				}
				Skip();
				if (_at >= _text.Length || _text[_at] != '}')
					throw Error("a ',' or '}' should be here");
				break;
			}
			_at++;
			obj.Length = _at - obj.Start;
			return obj;
		}

		private JArray Array()
		{
			var array = new JArray { Start = _at++ };
			while (true)
			{
				Skip();
				if (_at < _text.Length && _text[_at] == ']')
					break;
				array.Values.Add(Value());
				Skip();
				if (_at < _text.Length && _text[_at] == ',')
				{
					_at++;
					continue;
				}
				Skip();
				if (_at >= _text.Length || _text[_at] != ']')
					throw Error("a ',' or ']' should be here");
				break;
			}
			_at++;
			array.Length = _at - array.Start;
			return array;
		}

		private string String()
		{
			_at++;
			var value = new StringBuilder();
			while (_at < _text.Length && _text[_at] != '"')
			{
				if (_text[_at] == '\\' && _at + 1 < _text.Length)
				{
					_at++;
					switch (_text[_at])
					{
						case 'n': value.Append('\n'); break;
						case 't': value.Append('\t'); break;
						case 'r': value.Append('\r'); break;
						case 'b': value.Append('\b'); break;
						case 'f': value.Append('\f'); break;
						case 'u' when _at + 4 < _text.Length:
							value.Append((char)int.Parse(_text.Substring(_at + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
							_at += 4;
							break;
						default: value.Append(_text[_at]); break;
					}
					_at++;
				}
				else
					value.Append(_text[_at++]);
			}
			if (_at >= _text.Length)
				throw Error("a string isn't closed");
			_at++;
			return value.ToString();
		}
	}
}
