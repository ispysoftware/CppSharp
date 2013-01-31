﻿using System;
using System.Collections.Generic;
using System.IO;
using Cxxi.Types;

namespace Cxxi.Generators.CLI
{
    public class CLIForwardRefeferencePrinter : IDeclVisitor<bool>
    {
        public readonly IList<string> Includes;
        public readonly IList<string> Refs;

        public CLIForwardRefeferencePrinter()
        {
            Includes = new List<string>();
            Refs = new List<string>();
        }

        public bool VisitDeclaration(Declaration decl)
        {
            throw new NotImplementedException();
        }

        public bool VisitClassDecl(Class @class)
        {
            var completeDecl = @class.CompleteDeclaration as Class;
            if (@class.IsIncomplete && completeDecl != null)
                return VisitClassDecl(completeDecl);

            if (@class.IsValueType)
            {
                Refs.Add(string.Format("value struct {0};", @class.Name));
                return true;
            }

            Refs.Add(string.Format("ref class {0};", @class.Name));
            return true;
        }

        public bool VisitFieldDecl(Field field)
        {
            Class @class;
            if (field.Type.IsTagDecl(out @class))
            {
                if (@class.IsValueType)
                    Includes.Add(GetHeaderFromDecl(@class));
                else
                    VisitClassDecl(@class);

                return true;
            }

            Enumeration @enum;
            if (field.Type.IsTagDecl(out @enum))
                return VisitEnumDecl(@enum);

            Includes.Add(GetHeaderFromDecl(field));
            return true;
        }

        public bool VisitFunctionDecl(Function function)
        {
            throw new NotImplementedException();
        }

        public bool VisitMethodDecl(Method method)
        {
            throw new NotImplementedException();
        }

        public bool VisitParameterDecl(Parameter parameter)
        {
            throw new NotImplementedException();
        }

        public string GetHeaderFromDecl(Declaration decl)
        {
            var @namespace = decl.Namespace;
            var unit = @namespace.TranslationUnit;

            if (unit.Ignore)
                return string.Empty;

            if (unit.IsSystemHeader)
                return string.Empty;

            return Path.GetFileNameWithoutExtension(unit.FileName);
        }

        public bool VisitTypedefDecl(TypedefDecl typedef)
        {
            FunctionType function;
            if (typedef.Type.IsPointerTo<FunctionType>(out function))
            {
                Includes.Add(GetHeaderFromDecl(typedef));
                return true;
            }

            throw new NotImplementedException();
        }

        public bool VisitEnumDecl(Enumeration @enum)
        {
            if (@enum.Type.IsPrimitiveType(PrimitiveType.Int32))
            {
                Refs.Add(string.Format("enum struct {0};", @enum.Name));
                return true;
            }

            Refs.Add(string.Format("enum struct {0} : {1};", @enum.Name,
                @enum.Type));
            return true;
        }

        public bool VisitClassTemplateDecl(ClassTemplate template)
        {
            throw new NotImplementedException();
        }

        public bool VisitFunctionTemplateDecl(FunctionTemplate template)
        {
            throw new NotImplementedException();
        }

        public bool VisitMacroDefinition(MacroDefinition macro)
        {
            throw new NotImplementedException();
        }

        public bool VisitNamespace(Namespace @namespace)
        {
            throw new NotImplementedException();
        }
    }

    #region CLI Text Templates
    public abstract class CLITextTemplate : TextTemplate
    {
        protected const string DefaultIndent = "    ";
        protected const uint MaxIndent = 80;

        public CLITypePrinter TypeSig { get; set; }

        public static string SafeIdentifier(string proposedName)
        {
            return proposedName;
        }

        public string QualifiedIdentifier(Declaration decl)
        {
            return string.Format("{0}::{1}", Library.Name, decl.Name);
        }

        public void GenerateStart()
        {
            if (Transform == null)
            {
                WriteLine("//----------------------------------------------------------------------------");
                WriteLine("// This is autogenerated code by cxxi-generator.");
                WriteLine("// Do not edit this file or all your changes will be lost after re-generation.");
                WriteLine("//----------------------------------------------------------------------------");

                if (FileExtension == "cpp")
                    WriteLine(@"#include ""../interop.h""          // marshalString");
            }
            else
            {
                Transform.GenerateStart(this);
            }
        }

        public void GenerateAfterNamespaces()
        {
            if (Transform != null)
                Transform.GenerateAfterNamespaces(this);
        }

        public void GenerateSummary(string comment)
        {
            if (String.IsNullOrWhiteSpace(comment))
                return;

            // Wrap the comment to the line width.
            var maxSize = (int)(MaxIndent - CurrentIndent.Count - "/// ".Length);
            var lines = StringHelpers.WordWrapLines(comment, maxSize);

            WriteLine("/// <summary>");
            foreach (string line in lines)
                WriteLine(string.Format("/// {0}", line.TrimEnd()));
            WriteLine("/// </summary>");
        }

        public void GenerateInlineSummary(string comment)
        {
            if (String.IsNullOrWhiteSpace(comment))
                return;
            WriteLine("/// <summary> {0} </summary>", comment);
        }

        public void GenerateMethodParameters(Method method)
        {
            for (var i = 0; i < method.Parameters.Count; ++i)
            {
                if (method.Conversion == MethodConversionKind.FunctionToInstanceMethod
                    && i == 0)
                    continue;

                var param = method.Parameters[i];
                Write("{0}", TypeSig.GetArgumentString(param));
                if (i < method.Parameters.Count - 1)
                    Write(", ");
            }
        }

        public static bool CheckIgnoreMethod(Class @class, Method method)
        {
            if (method.Ignore) return true;

            if (@class.IsAbstract && method.IsConstructor)
                return true;

            if (@class.IsValueType && method.IsDefaultConstructor)
                return true;

            if (method.IsCopyConstructor || method.IsMoveConstructor)
                return true;

            if (method.IsDestructor)
                return true;

            if (method.OperatorKind == CXXOperatorKind.Equal)
                return true;

            if (method.Kind == CXXMethodKind.Conversion)
                return true;

            if (method.Access != AccessSpecifier.Public)
                return true;

            return false;
        }

        public static bool CheckIgnoreField(Class @class, Field field)
        {
            if (field.Ignore) return true;

            return false;
        }

        public abstract override string FileExtension { get; }

        protected abstract override void Generate();
    }
    #endregion

    public class CLIGenerator : ILanguageGenerator
    {
        public Options Options { get; set; }
        public Library Library { get; set; }
        public ILibrary Transform { get; set; }
        public ITypeMapDatabase TypeMapDatabase { get; set; }
        public Generator Generator { get; set; }

        private readonly CLITypePrinter typePrinter;

        public CLIGenerator(Generator generator)
        {
            Generator = generator;
            typePrinter = new CLITypePrinter(generator);
            Type.TypePrinter = typePrinter;
        }

        T CreateTemplate<T>(TranslationUnit unit) where T : CLITextTemplate, new()
        {
            var template = new T
            {
                Generator = Generator,
                Options = Options,
                Library = Library,
                Transform = Transform,
                Module = unit,
                TypeSig = typePrinter
            };

            return template;
        }

        public static String WrapperSuffix = "_wrapper";
        
        void WriteTemplate(TextTemplate template)
        {
            var file = Path.GetFileNameWithoutExtension(template.Module.FileName) + WrapperSuffix + "."
                + template.FileExtension;

            var path = Path.Combine(Options.OutputDir, file);

            Console.WriteLine("  Generated '" + file + "'.");
            File.WriteAllText(Path.GetFullPath(path), template.ToString());
        }

        public bool Generate(TranslationUnit unit)
        {
            typePrinter.Library = Library;

            var header = CreateTemplate<CLIHeadersTemplate>(unit);
            WriteTemplate(header);

            var source = CreateTemplate<CLISourcesTemplate>(unit);
            WriteTemplate(source);

            return true;
        }
    }
}