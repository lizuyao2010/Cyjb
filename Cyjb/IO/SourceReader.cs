﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Cyjb.Text;

namespace Cyjb.IO
{
	/// <summary>
	/// 表示支持行列计数的源文件读取器。
	/// </summary>
	public sealed class SourceReader : IDisposable
	{
		/// <summary>
		/// 缓冲区的大小。
		/// </summary>
		private const int BufferSize = 0x200;
		/// <summary>
		/// 当前存有数据的缓冲区的指针。
		/// </summary>
		private SourceBuffer current = null;
		/// <summary>
		/// 最后一个存有数据的缓冲区的指针。
		/// </summary>
		private SourceBuffer last = null;
		/// <summary>
		/// 第一个存有数据的缓冲区的指针。
		/// </summary>
		private SourceBuffer first = null;
		/// <summary>
		/// 文本的读取器。
		/// </summary>
		private TextReader reader = null;
		/// <summary>
		/// 当前的字符索引。
		/// </summary>
		private int index = 0;
		/// <summary>
		/// 全局字符索引。
		/// </summary>
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int globalIndex = 0;
		/// <summary>
		/// 当前缓冲区的字符长度。
		/// </summary>
		private int len = 0;
		/// <summary>
		/// 第一块缓冲区的字符索引。
		/// </summary>
		private int firstIndex = 0;
		/// <summary>
		/// 最后一块缓冲区的字符长度。
		/// </summary>
		private int lastLen = 0;
		/// <summary>
		/// 用于构造字符串的 StringBuilder 实例。
		/// </summary>
		private StringBuilder builder = null;
		/// <summary>
		/// 源代码位置计数器。
		/// </summary>
		private SourceLocator locator;
		/// <summary>
		/// 使用指定的字符读取器初始化 <see cref="SourceReader"/> 类的新实例。
		/// </summary>
		/// <param name="reader">用于读取源文件的字符读取器。</param>
		public SourceReader(TextReader reader) : this(reader, 4) { }
		/// <summary>
		/// 使用指定的字符读取器和 Tab 宽度初始化 <see cref="SourceReader"/> 类的新实例。
		/// </summary>
		/// <param name="reader">用于读取源文件的字符读取器。</param>
		/// <param name="tabSize">Tab 的宽度。</param>
		public SourceReader(TextReader reader, int tabSize)
		{
			ExceptionHelper.CheckArgumentNull(reader, "reader");
			locator = new SourceLocator(tabSize);
			this.reader = reader;
			current = first = last = new SourceBuffer();
			firstIndex = lastLen = 0;
			current.Buffer = new char[BufferSize];
			current.Next = current;
		}
		/// <summary>
		/// 获取基础的字符读取器。
		/// </summary>
		public TextReader BaseReader
		{
			get { return reader; }
		}
		/// <summary>
		/// 当前的字符索引。
		/// </summary>
		public int Index
		{
			get { return globalIndex; }
		}
		/// <summary>
		/// 起始索引之前的源代码位置（不包括被丢弃的字符）。
		/// </summary>
		public SourceLocation BeforeStartLocation
		{
			get { return locator.Location; }
		}
		/// <summary>
		/// 起始索引的源代码位置（不包括被丢弃的字符）。
		/// </summary>
		public SourceLocation StartLocation
		{
			get { return locator.NextLocation; }
		}
		/// <summary>
		/// 关闭 <see cref="SourceReader"/> 对象和基础字符读取器，并释放与读取器关联的所有系统资源。
		/// </summary>
		public void Close()
		{
			this.Dispose();
		}

		#region IDisposable 成员

		/// <summary>
		/// 执行与释放或重置非托管资源相关的应用程序定义的任务。
		/// </summary>
		public void Dispose()
		{
			if (reader != null)
			{
				reader.Dispose();
				reader = null;
			}
			GC.SuppressFinalize(this);
		}

		#endregion

		#region 读取字符

		/// <summary>
		/// 返回下一个可用的字符，但不使用它。
		/// </summary>
		/// <returns>表示下一个要读取的字符的整数，或者如果没有要读取的字符，则为 <c>-1</c>。</returns>
		public int Peek()
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (index == len)
			{
				if (!SwitchNextBuffer())
				{
					return -1;
				}
			}
			return current.Buffer[index];
		}
		/// <summary>
		/// 返回文本读取器中之后的 <paramref name="idx"/> 索引的字符，但不使用它。
		/// Peek(0) 就相当于 Peek()，但效率不如 Peek()。
		/// </summary>
		/// <returns>文本读取器中之后的 <paramref name="idx"/> 索引的字符，
		/// 或为 <c>-1</c>（如果没有更多的可用字符）。</returns>
		public int Peek(int idx)
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (idx < 0)
			{
				throw ExceptionHelper.ArgumentOutOfRange("count");
			}
			SourceBuffer temp = current;
			int tempLen = len;
			idx += index;
			while (true)
			{
				if (idx >= tempLen)
				{
					idx -= tempLen;
					if (temp == last && (tempLen = PrepareBuffer()) == 0)
					{
						// 没有可读数据了，返回。
						return -1;
					}
					temp = temp.Next;
				}
				else
				{
					return temp.Buffer[idx];
				}
			}
		}
		/// <summary>
		/// 读取文本读取器中的下一个字符并使该字符的位置提升一个字符。
		/// </summary>
		/// <returns>文本读取器中的下一个字符，或为 <c>-1</c>（如果没有更多的可用字符）。</returns>
		public int Read()
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (index == len)
			{
				if (!SwitchNextBuffer())
				{
					return -1;
				}
			}
			globalIndex++;
			return current.Buffer[index++];
		}
		/// <summary>
		/// 读取文本读取器中之后的 <paramref name="idx"/> 索引的字符，并使该字符的位置提升。
		/// Read(0) 就相当于 Read()，但效率不如 Read()。
		/// </summary>
		/// <returns>文本读取器中之后的 <paramref name="idx"/> 索引的字符，
		/// 或为 <c>-1</c>（如果没有更多的可用字符）。</returns>
		public int Read(int idx)
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (idx < 0)
			{
				throw ExceptionHelper.ArgumentOutOfRange("idx");
			}
			while (true)
			{
				if (idx >= len - index)
				{
					globalIndex += len - index;
					idx -= len - index;
					index = len;
					if (!SwitchNextBuffer())
					{
						// 没有数据了，返回。
						index = len;
						return -1;
					}
				}
				else
				{
					globalIndex += idx + 1;
					index += idx;
					return current.Buffer[index++];
				}
			}
		}
		/// <summary>
		/// 回退最后被读取的字符，只有之前的数据未被丢弃时才可以进行回退。
		/// </summary>
		/// <returns>如果回退成功，则为 <c>true</c>；否则为 <c>false</c>。</returns>
		public bool Unget()
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (current != first)
			{
				if (index <= 0)
				{
					SwitchPrevBuffer();
				}
				globalIndex--;
				index--;
				return true;
			}
			else if (index > firstIndex)
			{
				globalIndex--;
				index--;
				return true;
			}
			return false;
		}
		/// <summary>
		/// 回退 <paramref name="count"/> 个字符，只有之前的数据未被丢弃时才可以进行回退。
		/// Unget(1) 相当于 Unget()，但效率不如 Unget()。
		/// </summary>
		/// <param name="count">要回退的字符个数。</param>
		/// <returns>实际回退的字符个数，小于等于 <paramref name="count"/>。</returns>
		public int Unget(int count)
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (count < 0)
			{
				throw ExceptionHelper.ArgumentOutOfRange("count");
			}
			if (count == 0)
			{
				return 0;
			}
			int backCount = 0;
			while (true)
			{
				if (current == first)
				{
					int charCnt = index - firstIndex;
					if (count > charCnt)
					{
						backCount += charCnt;
						count -= charCnt;
						index = firstIndex;
					}
					else
					{
						backCount += count;
						index -= count;
					}
					break;
				}
				else
				{
					if (count > index)
					{
						backCount += index;
						count -= index;
						SwitchPrevBuffer();
					}
					else
					{
						backCount += count;
						index -= count;
						break;
					}
				}
			}
			globalIndex -= backCount;
			return backCount;
		}
		/// <summary>
		/// 将当前位置之前的数据全部丢弃，之后的 <see cref="Unget()"/> 操作至多回退到当前位置。
		/// </summary>
		public void Drop()
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			while (first != current)
			{
				locator.Forward(first.Buffer, firstIndex, BufferSize - firstIndex);
				firstIndex = 0;
				first = first.Next;
			}
			locator.Forward(current.Buffer, firstIndex, index - firstIndex);
			firstIndex = index;
		}
		/// <summary>
		/// 将当前位置之前的数据全部丢弃，并返回被丢弃的数据。
		/// 之后的 <see cref="Unget()"/> 操作至多回退到当前位置。
		/// </summary>
		/// <returns>当前位置之前的数据。</returns>
		public string Accept()
		{
			if (reader == null)
			{
				throw ExceptionHelper.SourceReaderClosed();
			}
			if (builder == null)
			{
				builder = new StringBuilder();
			}
			else
			{
				builder.Length = 0;
			}
			builder.EnsureCapacity(GetDropSize());
			// 将字符串复制到 StringBuilder 中。
			while (first != current)
			{
				builder.Append(first.Buffer, firstIndex, BufferSize - firstIndex);
				locator.Forward(first.Buffer, firstIndex, BufferSize - firstIndex);
				firstIndex = 0;
				first = first.Next;
			}
			builder.Append(current.Buffer, firstIndex, index - firstIndex);
			locator.Forward(current.Buffer, firstIndex, index - firstIndex);
			firstIndex = index;
			return builder.ToString();
		}
		/// <summary>
		/// 将当前位置之前的数据全部丢弃，并以 <see cref="Cyjb.Text.Token"/> 
		/// 的形式返回被丢弃的数据。
		/// 之后的 <see cref="Unget()"/> 操作至多回退到当前位置。
		/// </summary>
		/// <param name="tokenIndex">返回的 <see cref="Cyjb.Text.Token"/> 的字符索引。</param>
		/// <returns>当前位置之前的数据。</returns>
		public Token AcceptToken(int tokenIndex)
		{
			return AcceptToken(tokenIndex, null);
		}
		/// <summary>
		/// 将当前位置之前的数据全部丢弃，并以 <see cref="Cyjb.Text.Token"/> 
		/// 的形式返回被丢弃的数据。
		/// 之后的 <see cref="Unget()"/> 操作至多回退到当前位置。
		/// </summary>
		/// <param name="tokenIndex">返回的 <see cref="Cyjb.Text.Token"/> 的字符索引。</param>
		/// <param name="value"><see cref="Cyjb.Text.Token"/> 的值。</param>
		/// <returns>当前位置之前的数据。</returns>
		public Token AcceptToken(int tokenIndex, object value)
		{
			SourceLocation start = locator.NextLocation;
			return new Token(tokenIndex, Accept(), start, locator.Location, value);
		}

		#endregion // 读取字符

		#region 缓冲区操作

		/// <summary>
		/// 估算需要被抛弃的字符个数。
		/// </summary>
		/// <returns>要被抛弃的字符个数的估算值。</returns>
		private int GetDropSize()
		{
			SourceBuffer temp = first;
			int size = BufferSize;
			while (temp != current)
			{
				size += BufferSize;
				temp = temp.Next;
			}
			return size;
		}
		/// <summary>
		/// 切换到下一块缓冲区。如果没有有效的数据，则从基础字符读取器中读取字符，并填充到缓冲区中。
		/// </summary>
		/// <returns>如果切换成功，则为 <c>true</c>；否则为 <c>false</c>。</returns>
		private bool SwitchNextBuffer()
		{
			Debug.Assert(index == len);
			Debug.Assert(reader != null);
			if (current == last)
			{
				// 下一块缓冲区没有数据，需要从基础字符读取器中读取。
				if (PrepareBuffer() == 0)
				{
					return false;
				}
				len = lastLen;
				current = last;
			}
			else
			{
				// 下一块缓冲区有数据，直接后移。
				current = current.Next;
				if (current == last)
				{
					len = lastLen;
				}
			}
			index = 0;
			return len > 0;
		}
		/// <summary>
		/// 从基础字符读取器中读取字符，并填充到新的缓冲区中。
		/// </summary>
		/// <returns>从基础字符读取器中读取的字符数量。</returns>
		private int PrepareBuffer()
		{
			if (reader.Peek() == -1)
			{
				return 0;
			}
			if (len > 0)
			{
				if (last.Next == first)
				{
					// 没有可用的空缓冲区，则需要新建立一块。
					SourceBuffer buffer = new SourceBuffer();
					buffer.Buffer = new char[BufferSize];
					buffer.Next = last.Next;
					buffer.Prev = current;
					last.Next.Prev = buffer;
					last.Next = buffer;
				}
				last = last.Next;
			}
			else
			{
				// len 为 0 应仅当 last == current 时。
				Debug.Assert(last == current);
			}
			lastLen = reader.ReadBlock(last.Buffer, 0, BufferSize);
			if (len == 0)
			{
				len = lastLen;
			}
			return lastLen;
		}
		/// <summary>
		/// 切换到上一块缓冲区。
		/// </summary>
		private void SwitchPrevBuffer()
		{
			Debug.Assert(current != first);
			current = current.Prev;
			index = len = BufferSize;
		}
		/// <summary>
		/// 表示 <see cref="Cyjb.IO.SourceReader"/> 的字符缓冲区。
		/// </summary>
		private sealed class SourceBuffer
		{
			/// <summary>
			/// 字符缓冲区。
			/// </summary>
			public char[] Buffer;
			/// <summary>
			/// 下一个字符缓冲区。
			/// </summary>
			public SourceBuffer Next;
			/// <summary>
			/// 上一个字符缓冲区。
			/// </summary>
			public SourceBuffer Prev;
			/// <summary>
			/// 返回当前对象的字符串表示形式。
			/// </summary>
			/// <returns>当前对象的字符串表示形式。</returns>
			public override string ToString()
			{
				return string.Concat("{", new string(Buffer), "}");
			}
		}

		#endregion // 缓冲区操作

	}
}
