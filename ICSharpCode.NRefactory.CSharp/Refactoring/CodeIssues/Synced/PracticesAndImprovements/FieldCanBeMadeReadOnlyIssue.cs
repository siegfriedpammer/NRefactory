//
// FieldCanBeMadeReadOnlyIssue.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2013 Xamarin Inc. (http://xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System.Collections.Generic;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.Refactoring;
using ICSharpCode.NRefactory.CSharp.Analysis;
using System.Linq;
using ICSharpCode.NRefactory.CSharp.Resolver;
using System.Threading;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using System;
using System.Diagnostics;
using ICSharpCode.NRefactory.Utils;

namespace ICSharpCode.NRefactory.CSharp.Refactoring
{
	[IssueDescription("Convert field to readonly",
	                  Description = "Convert field to readonly",
	                  Category = IssueCategories.PracticesAndImprovements,
	                  Severity = Severity.Suggestion,
	                  ResharperDisableKeyword = "FieldCanBeMadeReadOnly.Local")]
	public class FieldCanBeMadeReadOnlyIssue : GatherVisitorCodeIssueProvider
	{
		protected override IGatherVisitor CreateVisitor(BaseRefactoringContext context)
		{
			return new GatherVisitor(context);
		}

		class GatherVisitor : GatherVisitorBase<FieldCanBeMadeReadOnlyIssue>
		{
			HashSet<string> skipVariable = new HashSet<string>();
			List<VariableInitializer> potentialReadonlyFields = new List<VariableInitializer>();

			public GatherVisitor(BaseRefactoringContext context) : base (context)
			{
			}

			void Collect()
			{
				foreach (var varDecl in potentialReadonlyFields) {
					AddIssue(
						varDecl.NameToken,
						ctx.TranslateString("Convert to readonly"),
						ctx.TranslateString("To readonly"),
						script => {
							var constVarDecl = (FieldDeclaration)varDecl.Parent.Clone();
							constVarDecl.Modifiers |= Modifiers.Readonly;
							script.Replace(varDecl.Parent, constVarDecl);
						}
					);
				}
			}

			public override void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
			{
				foreach (var fieldDeclaration in typeDeclaration.Members.OfType<FieldDeclaration>()) {
					if (IsSuppressed(fieldDeclaration.StartLocation))
						continue;
					if (fieldDeclaration.HasModifier(Modifiers.Const) || fieldDeclaration.HasModifier(Modifiers.Readonly))
						continue;
					if (fieldDeclaration.HasModifier(Modifiers.Public) || fieldDeclaration.HasModifier(Modifiers.Protected) || fieldDeclaration.HasModifier(Modifiers.Internal))
						continue;
					var rr = ctx.Resolve(fieldDeclaration.ReturnType);
					if (rr.Type.IsReferenceType.HasValue && !rr.Type.IsReferenceType.Value)
						continue;
					if (fieldDeclaration.Variables.Count() > 1)
						continue;
					potentialReadonlyFields.AddRange(fieldDeclaration.Variables); 
				}
				base.VisitTypeDeclaration(typeDeclaration);
				Collect();
				potentialReadonlyFields.Clear();
			}

			public override void VisitMethodDeclaration(MethodDeclaration methodDeclaration)
			{
				skipVariable = new HashSet<string>(methodDeclaration.Parameters.Select(p => p.Name));
				base.VisitMethodDeclaration(methodDeclaration);
			}

			public override void VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration)
			{
//				skipVariable = new HashSet<string>(constructorDeclaration.Parameters.Select(p => p.Name));
//				base.VisitConstructorDeclaration(constructorDeclaration);
			}

			public override void VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration)
			{
				skipVariable = new HashSet<string>(operatorDeclaration.Parameters.Select(p => p.Name));
				base.VisitOperatorDeclaration(operatorDeclaration);
			}

			public override void VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration)
			{
				skipVariable = new HashSet<string>(indexerDeclaration.Parameters.Select(p => p.Name));
				base.VisitIndexerDeclaration(indexerDeclaration);
			}

			public override void VisitAnonymousMethodExpression(AnonymousMethodExpression anonymousMethodExpression)
			{
				var old = skipVariable;
				skipVariable = new HashSet<string>(skipVariable);
				foreach (var p in anonymousMethodExpression.Parameters)
					skipVariable.Add(p.Name); 
				base.VisitAnonymousMethodExpression(anonymousMethodExpression);
				skipVariable = old;
			}

			public override void VisitLambdaExpression(LambdaExpression lambdaExpression)
			{
				var old = skipVariable;
				skipVariable = new HashSet<string>(skipVariable);
				foreach (var p in lambdaExpression.Parameters)
					skipVariable.Add(p.Name); 
				base.VisitLambdaExpression(lambdaExpression);
				skipVariable = old;
			}

			public override void VisitBlockStatement(BlockStatement blockStatement)
			{
				base.VisitBlockStatement(blockStatement);
				if (blockStatement.Parent is EntityDeclaration || blockStatement.Parent is Accessor) {
					var assignmentAnalysis = new ConvertToConstantIssue.VariableAssignmentAnalysis (blockStatement, ctx.Resolver, ctx.CancellationToken);
					List<VariableInitializer> newVars = new List<VariableInitializer>();
					foreach (var variable in potentialReadonlyFields) {
						if (!skipVariable.Contains(variable.Name)) {
							var rr = ctx.Resolve(variable) as MemberResolveResult; 
							if (rr == null)
								continue;
							assignmentAnalysis.Analyze(rr.Member as IField, DefiniteAssignmentStatus.PotentiallyAssigned, ctx.CancellationToken);
							if (assignmentAnalysis.GetStatusAfter(blockStatement) == DefiniteAssignmentStatus.DefinitelyAssigned)
								continue;
						}
						newVars.Add(variable);
					}
					potentialReadonlyFields = newVars;
				}
			}

		}
	}
}

