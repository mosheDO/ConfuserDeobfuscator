/*
    Copyright (C) 2012-2013 de4dot@gmail.com

    Permission is hereby granted, free of charge, to any person obtaining
    a copy of this software and associated documentation files (the
    "Software"), to deal in the Software without restriction, including
    without limitation the rights to use, copy, modify, merge, publish,
    distribute, sublicense, and/or sell copies of the Software, and to
    permit persons to whom the Software is furnished to do so, subject to
    the following conditions:

    The above copyright notice and this permission notice shall be
    included in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
    IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
    CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
    TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
    SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

﻿using System;
using System.Collections.Generic;
using System.IO;

namespace dnlib.DotNet.Writer {
	/// <summary>
	/// Helps <see cref="SignatureWriter"/> map <see cref="ITypeDefOrRef"/>s to tokens
	/// </summary>
	public interface ISignatureWriterHelper {
		/// <summary>
		/// Returns a <c>TypeDefOrRef</c> encoded token
		/// </summary>
		/// <param name="typeDefOrRef">A <c>TypeDefOrRef</c> type</param>
		uint ToEncodedToken(ITypeDefOrRef typeDefOrRef);

		/// <summary>
		/// Called when an error is detected (eg. a null pointer). The error can be
		/// ignored but the signature won't be valid.
		/// </summary>
		/// <param name="message">Error message</param>
		void Error(string message);
	}

	/// <summary>
	/// Writes signatures
	/// </summary>
	public struct SignatureWriter : IDisposable {
		ISignatureWriterHelper helper;
		RecursionCounter recursionCounter;
		MemoryStream outStream;
		BinaryWriter writer;

		/// <summary>
		/// Write a <see cref="TypeSig"/> signature
		/// </summary>
		/// <param name="helper">Helper</param>
		/// <param name="typeSig">The type</param>
		/// <returns>The signature as a byte array</returns>
		public static byte[] Write(ISignatureWriterHelper helper, TypeSig typeSig) {
			using (var writer = new SignatureWriter(helper)) {
				writer.Write(typeSig);
				return writer.GetResult();
			}
		}

		/// <summary>
		/// Write a <see cref="CallingConventionSig"/> signature
		/// </summary>
		/// <param name="helper">Helper</param>
		/// <param name="sig">The signature</param>
		/// <returns>The signature as a byte array</returns>
		public static byte[] Write(ISignatureWriterHelper helper, CallingConventionSig sig) {
			using (var writer = new SignatureWriter(helper)) {
				writer.Write(sig);
				return writer.GetResult();
			}
		}

		SignatureWriter(ISignatureWriterHelper helper) {
			this.helper = helper;
			this.recursionCounter = new RecursionCounter();
			this.outStream = new MemoryStream();
			this.writer = new BinaryWriter(outStream);
		}

		byte[] GetResult() {
			return outStream.ToArray();
		}

		uint WriteCompressedUInt32(uint value) {
			if (value >= 0x1FFFFFFF) {
				helper.Error("UInt32 value is too big and can't be compressed");
				value = 0x1FFFFFFF;
			}
			writer.WriteCompressedUInt32(value);
			return value;
		}

		int WriteCompressedInt32(int value) {
			if (value < -0x10000000) {
				helper.Error("Int32 value is too small and can't be compressed.");
				value = -0x10000000;
			}
			else if (value > 0x0FFFFFFF) {
				helper.Error("Int32 value is too big and can't be compressed.");
				value = 0x0FFFFFFF;
			}
			writer.WriteCompressedInt32(value);
			return value;
		}

		void Write(TypeSig typeSig) {
			if (typeSig == null) {
				helper.Error("TypeSig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			writer.Write((byte)typeSig.ElementType);

			uint count;
			switch (typeSig.ElementType) {
			case ElementType.Void:
			case ElementType.Boolean:
			case ElementType.Char:
			case ElementType.I1:
			case ElementType.U1:
			case ElementType.I2:
			case ElementType.U2:
			case ElementType.I4:
			case ElementType.U4:
			case ElementType.I8:
			case ElementType.U8:
			case ElementType.R4:
			case ElementType.R8:
			case ElementType.String:
			case ElementType.TypedByRef:
			case ElementType.I:
			case ElementType.U:
			case ElementType.Object:
			case ElementType.Sentinel:
				break;

			case ElementType.Ptr:
			case ElementType.ByRef:
			case ElementType.SZArray:
			case ElementType.Pinned:
				Write(typeSig.Next);
				break;

			case ElementType.ValueType:
			case ElementType.Class:
				Write(((TypeDefOrRefSig)typeSig).TypeDefOrRef);
				break;

			case ElementType.Var:
			case ElementType.MVar:
				WriteCompressedUInt32(((GenericSig)typeSig).Number);
				break;

			case ElementType.Array:
				var ary = (ArraySig)typeSig;
				Write(ary.Next);
				WriteCompressedUInt32(ary.Rank);
				if (ary.Rank == 0)
					break;
				count = WriteCompressedUInt32((uint)ary.Sizes.Count);
				for (uint i = 0; i < count; i++)
					WriteCompressedUInt32(ary.Sizes[(int)i]);
				count = WriteCompressedUInt32((uint)ary.LowerBounds.Count);
				for (uint i = 0; i < count; i++)
					WriteCompressedInt32(ary.LowerBounds[(int)i]);
				break;

			case ElementType.GenericInst:
				var gis = (GenericInstSig)typeSig;
				Write(gis.GenericType);
				count = WriteCompressedUInt32((uint)gis.GenericArguments.Count);
				for (uint i = 0; i < count; i++)
					Write(gis.GenericArguments[(int)i]);
				break;

			case ElementType.ValueArray:
				Write(typeSig.Next);
				WriteCompressedUInt32((typeSig as ValueArraySig).Size);
				break;

			case ElementType.FnPtr:
				Write((typeSig as FnPtrSig).Signature);
				break;

			case ElementType.CModReqd:
			case ElementType.CModOpt:
				Write((typeSig as ModifierSig).Modifier);
				Write(typeSig.Next);
				break;

			case ElementType.Module:
				WriteCompressedUInt32((typeSig as ModuleSig).Index);
				Write(typeSig.Next);
				break;

			case ElementType.End:
			case ElementType.R:
			case ElementType.Internal:
			default:
				helper.Error("Unknown or unsupported element type");
				break;
			}

			recursionCounter.Decrement();
		}

		void Write(ITypeDefOrRef tdr) {
			if (tdr == null) {
				helper.Error("TypeDefOrRef is null");
				WriteCompressedUInt32(0);
				return;
			}

			uint encodedToken = helper.ToEncodedToken(tdr);
			if (encodedToken > 0x1FFFFFFF) {
				helper.Error("Encoded token is too big");
				encodedToken = 0;
			}
			WriteCompressedUInt32(encodedToken);
		}

		void Write(CallingConventionSig sig) {
			if (sig == null) {
				helper.Error("sig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			MethodBaseSig mbs;
			FieldSig fs;
			LocalSig ls;
			GenericInstMethodSig gim;

			if ((mbs = sig as MethodBaseSig) != null)
				Write(mbs);
			else if ((fs = sig as FieldSig) != null)
				Write(fs);
			else if ((ls = sig as LocalSig) != null)
				Write(ls);
			else if ((gim = sig as GenericInstMethodSig) != null)
				Write(gim);
			else {
				helper.Error("Unknown calling convention sig");
				writer.Write((byte)sig.GetCallingConvention());
			}

			recursionCounter.Decrement();
		}

		void Write(MethodBaseSig sig) {
			if (sig == null) {
				helper.Error("sig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			writer.Write((byte)sig.GetCallingConvention());
			if (sig.Generic)
				WriteCompressedUInt32(sig.GenParamCount);

			uint numParams = (uint)sig.Params.Count;
			if (sig.ParamsAfterSentinel != null)
				numParams += (uint)sig.ParamsAfterSentinel.Count;

			uint count = WriteCompressedUInt32(numParams);
			Write(sig.RetType);
			for (uint i = 0; i < count && i < (uint)sig.Params.Count; i++)
				Write(sig.Params[(int)i]);

			if (sig.ParamsAfterSentinel != null && sig.ParamsAfterSentinel.Count > 0) {
				writer.Write((byte)ElementType.Sentinel);
				for (uint i = 0, j = (uint)sig.Params.Count; i < (uint)sig.ParamsAfterSentinel.Count && j < count; i++, j++)
					Write(sig.ParamsAfterSentinel[(int)i]);
			}

			recursionCounter.Decrement();
		}

		void Write(FieldSig sig) {
			if (sig == null) {
				helper.Error("sig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			writer.Write((byte)sig.GetCallingConvention());
			Write(sig.Type);

			recursionCounter.Decrement();
		}

		void Write(LocalSig sig) {
			if (sig == null) {
				helper.Error("sig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			writer.Write((byte)sig.GetCallingConvention());
			uint count = WriteCompressedUInt32((uint)sig.Locals.Count);
			for (uint i = 0; i < count; i++)
				Write(sig.Locals[(int)i]);

			recursionCounter.Decrement();
		}

		void Write(GenericInstMethodSig sig) {
			if (sig == null) {
				helper.Error("sig is null");
				return;
			}
			if (!recursionCounter.Increment()) {
				helper.Error("Infinite recursion");
				return;
			}

			writer.Write((byte)sig.GetCallingConvention());
			uint count = WriteCompressedUInt32((uint)sig.GenericArguments.Count);
			for (uint i = 0; i < count; i++)
				Write(sig.GenericArguments[(int)i]);

			recursionCounter.Decrement();
		}

		/// <inheritdoc/>
		public void Dispose() {
			if (outStream != null)
				outStream.Dispose();
			if (writer != null)
				((IDisposable)writer).Dispose();
		}
	}
}
