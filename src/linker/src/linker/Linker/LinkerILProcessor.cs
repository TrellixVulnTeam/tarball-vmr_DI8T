﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mono.Linker
{
	class LinkerILProcessor
	{
		readonly ILProcessor _ilProcessor;

		Collections.Generic.Collection<Instruction> Instructions => _ilProcessor.Body.Instructions;

		internal LinkerILProcessor (MethodBody body)
		{
			_ilProcessor = body.GetILProcessor ();
		}

		public void Emit (OpCode opcode) => Append (_ilProcessor.Create (opcode));
		public void Emit (OpCode opcode, TypeReference type) => Append (_ilProcessor.Create (opcode, type));
		public void Emit (OpCode opcode, MethodReference method) => Append (_ilProcessor.Create (opcode, method));
		public void Emit (OpCode opcode, VariableDefinition variable) => Append (_ilProcessor.Create (opcode, variable));

		public void Emit (OpCode opcode, string value) => Append (_ilProcessor.Create (opcode, value));
		public void InsertBefore (Instruction target, Instruction instruction)
		{
			// When inserting before, all pointers have to be updated to the new "before" instruction.
			RedirectScopeStart (target, instruction);
			ReplaceInstructionReference (target, instruction);
			_ilProcessor.InsertBefore (target, instruction);
		}

		// When inserting after, no redirection is necessary since it will naturally be "appended" to the same blocks
		// as the target instruction.
		public void InsertAfter (Instruction target, Instruction instruction)
		{
			RedirectScopeEnd (target, instruction);
			_ilProcessor.InsertAfter (target, instruction);
		}

		public void Append (Instruction instruction)
		{
			Instruction lastInstruction = Instructions.Count == 0 ? null : Instructions[Instructions.Count - 1];
			RedirectScopeEnd (lastInstruction, instruction);
			_ilProcessor.Append (instruction);
		}

		public void Replace (Instruction target, Instruction instruction)
		{
			RedirectScopeStart (target, instruction);
			RedirectScopeEnd (target, instruction);
			ReplaceInstructionReference (target, instruction);
			_ilProcessor.Replace (target, instruction);
		}

		public void Replace (int index, Instruction instruction) => Replace (_ilProcessor.Body.Instructions[index], instruction);

		public void Remove (Instruction instruction)
		{
			int index = _ilProcessor.Body.Instructions.IndexOf (instruction);
			if (index == -1)
				throw new ArgumentOutOfRangeException (nameof (instruction));

			Instruction nextInstruction = Instructions.Count == index + 1 ? null : Instructions[index + 1];
			Instruction previousInstruction = index == 0 ? null : Instructions[index - 1];

			RedirectScopeStart (instruction, nextInstruction);
			RedirectScopeEnd (instruction, previousInstruction);
			ReplaceInstructionReference (instruction, nextInstruction);
			_ilProcessor.Remove (instruction);
		}

		public void RemoveAt (int index) => Remove (Instructions[index]);

		void RedirectScopeStart (Instruction oldTarget, Instruction newTarget)
		{
			// In Cecil "start" pointers point to the first instruction in a given scope
			// and the "end" pointers point to the first instruction after the given block
			// so they need to effectively point to the start of the next scope.
			// That's why both start and end are handled in the RedirectScopeStart.
			foreach (var handler in _ilProcessor.Body.ExceptionHandlers) {
				if (handler.TryStart == oldTarget)
					handler.TryStart = newTarget;
				if (handler.TryEnd == oldTarget)
					handler.TryEnd = newTarget;
				if (handler.HandlerStart == oldTarget)
					handler.HandlerStart = newTarget;
				if (handler.HandlerEnd == oldTarget)
					handler.HandlerEnd = newTarget;
				if (handler.FilterStart == oldTarget)
					handler.FilterStart = newTarget;
			}
		}

#pragma warning disable IDE0060 // Remove unused parameter
		static void RedirectScopeEnd (Instruction oldTarget, Instruction newTarget)
#pragma warning restore IDE0060 // Remove unused parameter
		{
			// Currently Cecil treats all block boundaries as "starts"
			// so nothing to do here.
		}

		void ReplaceInstructionReference (Instruction oldTarget, Instruction newTarget)
		{
			foreach (var instr in Instructions) {
				switch (instr.OpCode.FlowControl) {
				case FlowControl.Branch:
				case FlowControl.Cond_Branch:
					if (instr.Operand == oldTarget)
						instr.Operand = newTarget;
					break;
				}
			}
		}
	}

	static class ILProcessorExtensions
	{
		public static LinkerILProcessor GetLinkerILProcessor (this MethodBody body)
		{
			return new LinkerILProcessor (body);
		}
	}
}
