﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Represents a .NET assembly, consisting of one or more modules.
    /// </summary>
    internal abstract class AssemblySymbol : Symbol, IAssemblySymbolInternal
    {
        // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // Changes to the public interface of this class should remain synchronized with the VB version.
        // Do not make any changes to the public interface without making the corresponding change
        // to the VB version.
        // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

        /// <summary>
        /// The system assembly, which provides primitive types like Object, String, etc., e.g. mscorlib.dll. 
        /// The value is provided by ReferenceManager and must not be modified. For SourceAssemblySymbol, non-missing 
        /// coreLibrary must match one of the referenced assemblies returned by GetReferencedAssemblySymbols() method of 
        /// the main module. If there is no existing assembly that can be used as a source for the primitive types, 
        /// the value is a Compilation.MissingCorLibrary. 
        /// </summary>
        private AssemblySymbol _corLibrary;

        /// <summary>
        /// The system assembly, which provides primitive types like Object, String, etc., e.g. mscorlib.dll. 
        /// The value is MissingAssemblySymbol if none of the referenced assemblies can be used as a source for the 
        /// primitive types and the owning assembly cannot be used as the source too. Otherwise, it is one of 
        /// the referenced assemblies returned by GetReferencedAssemblySymbols() method or the owning assembly.
        /// </summary>
        internal AssemblySymbol CorLibrary
        {
            get
            {
                return _corLibrary;
            }
        }

        /// <summary>
        /// A helper method for ReferenceManager to set the system assembly, which provides primitive 
        /// types like Object, String, etc., e.g. mscorlib.dll. 
        /// </summary>
        internal void SetCorLibrary(AssemblySymbol corLibrary)
        {
            Debug.Assert((object)_corLibrary == null);
            _corLibrary = corLibrary;
        }

        /// <summary>
        /// Simple name the assembly.
        /// </summary>
        /// <remarks>
        /// This is equivalent to <see cref="Identity"/>.<see cref="AssemblyIdentity.Name"/>, but may be 
        /// much faster to retrieve for source code assemblies, since it does not require binding
        /// the assembly-level attributes that contain the version number and other assembly
        /// information.
        /// </remarks>
        public override string Name
        {
            get
            {
                return Identity.Name;
            }
        }

        /// <summary>
        /// Gets the identity of this assembly.
        /// </summary>
        public abstract AssemblyIdentity Identity { get; }

        AssemblyIdentity IAssemblySymbolInternal.Identity => Identity;

        IAssemblySymbolInternal IAssemblySymbolInternal.CorLibrary => CorLibrary;

        /// <summary>
        /// Assembly version pattern with wildcards represented by <see cref="ushort.MaxValue"/>,
        /// or null if the version string specified in the <see cref="AssemblyVersionAttribute"/> doesn't contain a wildcard.
        /// 
        /// For example, 
        ///   AssemblyVersion("1.2.*") is represented as 1.2.65535.65535,
        ///   AssemblyVersion("1.2.3.*") is represented as 1.2.3.65535.
        /// </summary>
        public abstract Version AssemblyVersionPattern { get; }

        /// <summary>
        /// Target architecture of the machine.
        /// </summary>
        internal Machine Machine
        {
            get
            {
                return Modules[0].Machine;
            }
        }

        /// <summary>
        /// Indicates that this PE file makes Win32 calls. See CorPEKind.pe32BitRequired for more information (http://msdn.microsoft.com/en-us/library/ms230275.aspx).
        /// </summary>
        internal bool Bit32Required
        {
            get
            {
                return Modules[0].Bit32Required;
            }
        }

        /// <summary>
        /// Gets the merged root namespace that contains all namespaces and types defined in the modules
        /// of this assembly. If there is just one module in this assembly, this property just returns the 
        /// GlobalNamespace of that module.
        /// </summary>
        public abstract NamespaceSymbol GlobalNamespace
        {
            get;
        }

        /// <summary>
        /// Given a namespace symbol, returns the corresponding assembly specific namespace symbol
        /// </summary>
        internal NamespaceSymbol GetAssemblyNamespace(NamespaceSymbol namespaceSymbol)
        {
            if (namespaceSymbol.IsGlobalNamespace)
            {
                return this.GlobalNamespace;
            }

            NamespaceSymbol container = namespaceSymbol.ContainingNamespace;

            if ((object)container == null)
            {
                return this.GlobalNamespace;
            }

            if (namespaceSymbol.NamespaceKind == NamespaceKind.Assembly && namespaceSymbol.ContainingAssembly == this)
            {
                // this is already the correct assembly namespace
                return namespaceSymbol;
            }

            NamespaceSymbol assemblyContainer = GetAssemblyNamespace(container);

            if ((object)assemblyContainer == (object)container)
            {
                // Trivial case, container isn't merged.
                return namespaceSymbol;
            }

            if ((object)assemblyContainer == null)
            {
                return null;
            }

            return assemblyContainer.GetNestedNamespace(namespaceSymbol.Name);
        }

        /// <summary>
        /// Gets a read-only list of all the modules in this assembly. (There must be at least one.) The first one is the main module
        /// that holds the assembly manifest.
        /// </summary>
        public abstract ImmutableArray<ModuleSymbol> Modules { get; }

        internal override TResult Accept<TArgument, TResult>(CSharpSymbolVisitor<TArgument, TResult> visitor, TArgument argument)
        {
            return visitor.VisitAssembly(this, argument);
        }

        public override void Accept(CSharpSymbolVisitor visitor)
        {
            visitor.VisitAssembly(this);
        }

        public override TResult Accept<TResult>(CSharpSymbolVisitor<TResult> visitor)
        {
            return visitor.VisitAssembly(this);
        }

        public sealed override SymbolKind Kind
        {
            get
            {
                return SymbolKind.Assembly;
            }
        }

        public sealed override AssemblySymbol ContainingAssembly
        {
            get
            {
                return null;
            }
        }

        // Only the compiler can create AssemblySymbols.
        internal AssemblySymbol()
        {
        }

        /// <summary>
        /// Does this symbol represent a missing assembly.
        /// </summary>
        internal abstract bool IsMissing
        {
            get;
        }

        public sealed override Accessibility DeclaredAccessibility
        {
            get
            {
                return Accessibility.NotApplicable;
            }
        }

        public sealed override bool IsStatic
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsVirtual
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsOverride
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsAbstract
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsSealed
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsExtern
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns data decoded from Obsolete attribute or null if there is no Obsolete attribute.
        /// This property returns ObsoleteAttributeData.Uninitialized if attribute arguments haven't been decoded yet.
        /// </summary>
        internal sealed override ObsoleteAttributeData ObsoleteAttributeData
        {
            get { return null; }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray<SyntaxReference>.Empty;
            }
        }

        /// <summary>
        /// True if the assembly contains interactive code.
        /// </summary>
        public virtual bool IsInteractive
        {
            get
            {
                return false;
            }
        }

        public sealed override Symbol ContainingSymbol
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Lookup a top level type referenced from metadata, names should be
        /// compared case-sensitively.
        /// </summary>
        /// <param name="emittedName">
        /// Full type name with generic name mangling.
        /// </param>
        /// <param name="digThroughForwardedTypes">
        /// Take forwarded types into account.
        /// </param>
        /// <remarks></remarks>
        internal NamedTypeSymbol LookupTopLevelMetadataType(ref MetadataTypeName emittedName, bool digThroughForwardedTypes)
        {
            return LookupTopLevelMetadataTypeWithCycleDetection(ref emittedName, visitedAssemblies: null, digThroughForwardedTypes: digThroughForwardedTypes);
        }

        /// <summary>
        /// Lookup a top level type referenced from metadata, names should be
        /// compared case-sensitively.  Detect cycles during lookup.
        /// </summary>
        /// <param name="emittedName">
        /// Full type name, possibly with generic name mangling.
        /// </param>
        /// <param name="visitedAssemblies">
        /// List of assemblies lookup has already visited (since type forwarding can introduce cycles).
        /// </param>
        /// <param name="digThroughForwardedTypes">
        /// Take forwarded types into account.
        /// </param>
        internal abstract NamedTypeSymbol LookupTopLevelMetadataTypeWithCycleDetection(ref MetadataTypeName emittedName, ConsList<AssemblySymbol> visitedAssemblies, bool digThroughForwardedTypes);

        /// <summary>
        /// Returns the type symbol for a forwarded type based its canonical CLR metadata name.
        /// The name should refer to a non-nested type. If type with this name is not forwarded,
        /// null is returned.
        /// </summary>
        public NamedTypeSymbol ResolveForwardedType(string fullyQualifiedMetadataName)
        {
            if (fullyQualifiedMetadataName == null)
            {
                throw new ArgumentNullException(nameof(fullyQualifiedMetadataName));
            }

            var emittedName = MetadataTypeName.FromFullName(fullyQualifiedMetadataName);
            return TryLookupForwardedMetadataType(ref emittedName);
        }

        /// <summary>
        /// Look up the given metadata type, if it is forwarded.
        /// </summary>
        internal NamedTypeSymbol TryLookupForwardedMetadataType(ref MetadataTypeName emittedName)
        {
            return TryLookupForwardedMetadataTypeWithCycleDetection(ref emittedName, visitedAssemblies: null);
        }

        /// <summary>
        /// Look up the given metadata type, if it is forwarded.
        /// </summary>
        internal virtual NamedTypeSymbol TryLookupForwardedMetadataTypeWithCycleDetection(ref MetadataTypeName emittedName, ConsList<AssemblySymbol> visitedAssemblies)
        {
            return null;
        }

        internal ErrorTypeSymbol CreateCycleInTypeForwarderErrorTypeSymbol(ref MetadataTypeName emittedName)
        {
            DiagnosticInfo diagnosticInfo = new CSDiagnosticInfo(ErrorCode.ERR_CycleInTypeForwarder, emittedName.FullName, this.Name);
            return new MissingMetadataTypeSymbol.TopLevel(this.Modules[0], ref emittedName, diagnosticInfo);
        }

        internal ErrorTypeSymbol CreateMultipleForwardingErrorTypeSymbol(ref MetadataTypeName emittedName, ModuleSymbol forwardingModule, AssemblySymbol destination1, AssemblySymbol destination2)
        {
            var diagnosticInfo = new CSDiagnosticInfo(ErrorCode.ERR_TypeForwardedToMultipleAssemblies, forwardingModule, this, emittedName.FullName, destination1, destination2);
            return new MissingMetadataTypeSymbol.TopLevel(forwardingModule, ref emittedName, diagnosticInfo);
        }

        internal abstract IEnumerable<NamedTypeSymbol> GetAllTopLevelForwardedTypes();

        /// <summary>
        /// Lookup declaration for predefined CorLib type in this Assembly.
        /// </summary>
        /// <returns>The symbol for the pre-defined type or an error type if the type is not defined in the core library.</returns>
        internal abstract NamedTypeSymbol GetDeclaredSpecialType(SpecialType type);

        /// <summary>
        /// Register declaration of predefined CorLib type in this Assembly.
        /// </summary>
        /// <param name="corType"></param>
        internal virtual void RegisterDeclaredSpecialType(NamedTypeSymbol corType)
        {
            throw ExceptionUtilities.Unreachable;
        }

        /// <summary>
        /// Continue looking for declaration of predefined CorLib type in this Assembly
        /// while symbols for new type declarations are constructed.
        /// </summary>
        internal virtual bool KeepLookingForDeclaredSpecialTypes
        {
            get
            {
                throw ExceptionUtilities.Unreachable;
            }
        }

        /// <summary>
        /// Return the native integer type corresponding to the underlying type.
        /// </summary>
        internal virtual NamedTypeSymbol GetNativeIntegerType(NamedTypeSymbol underlyingType)
        {
            throw ExceptionUtilities.Unreachable;
        }

        /// <summary>
        /// Figure out if the target runtime supports default interface implementation.
        /// </summary>
        internal bool RuntimeSupportsDefaultInterfaceImplementation
        {
            get => RuntimeSupportsFeature(SpecialMember.System_Runtime_CompilerServices_RuntimeFeature__DefaultImplementationsOfInterfaces);
        }

        /// <summary>
        /// Figure out if the target runtime supports static abstract members in interfaces.
        /// </summary>
        internal bool RuntimeSupportsStaticAbstractMembersInInterfaces
        {
            get => RuntimeSupportsFeature(SpecialMember.System_Runtime_CompilerServices_RuntimeFeature__VirtualStaticsInInterfaces);
        }

        private bool RuntimeSupportsFeature(SpecialMember feature)
        {
            Debug.Assert((SpecialType)SpecialMembers.GetDescriptor(feature).DeclaringTypeId == SpecialType.System_Runtime_CompilerServices_RuntimeFeature);
            return GetSpecialType(SpecialType.System_Runtime_CompilerServices_RuntimeFeature) is { TypeKind: TypeKind.Class, IsStatic: true } &&
                   GetSpecialTypeMember(feature) is object;
        }

        internal bool RuntimeSupportsUnmanagedSignatureCallingConvention
            => RuntimeSupportsFeature(SpecialMember.System_Runtime_CompilerServices_RuntimeFeature__UnmanagedSignatureCallingConvention);

        /// <summary>
        /// True if the target runtime support covariant returns of methods declared in classes.
        /// </summary>
        internal bool RuntimeSupportsCovariantReturnsOfClasses
        {
            get
            {
                // check for the runtime feature indicator and the required attribute.
                return
                    RuntimeSupportsFeature(SpecialMember.System_Runtime_CompilerServices_RuntimeFeature__CovariantReturnsOfClasses) &&
                    GetSpecialType(SpecialType.System_Runtime_CompilerServices_PreserveBaseOverridesAttribute) is { TypeKind: TypeKind.Class };
            }
        }

        /// <summary>
        /// Return an array of assemblies involved in canonical type resolution of
        /// NoPia local types defined within this assembly. In other words, all 
        /// references used by previous compilation referencing this assembly.
        /// </summary>
        /// <returns></returns>
        internal abstract ImmutableArray<AssemblySymbol> GetNoPiaResolutionAssemblies();
        internal abstract void SetNoPiaResolutionAssemblies(ImmutableArray<AssemblySymbol> assemblies);

        /// <summary>
        /// Return an array of assemblies referenced by this assembly, which are linked (/l-ed) by 
        /// each compilation that is using this AssemblySymbol as a reference. 
        /// If this AssemblySymbol is linked too, it will be in this array too.
        /// </summary>
        internal abstract ImmutableArray<AssemblySymbol> GetLinkedReferencedAssemblies();
        internal abstract void SetLinkedReferencedAssemblies(ImmutableArray<AssemblySymbol> assemblies);

        internal abstract IEnumerable<ImmutableArray<byte>> GetInternalsVisibleToPublicKeys(string simpleName);
        internal abstract bool AreInternalsVisibleToThisAssembly(AssemblySymbol other);

        /// <summary>
        /// Assembly is /l-ed by compilation that is using it as a reference.
        /// </summary>
        internal abstract bool IsLinked { get; }

        /// <summary>
        /// Returns true and a string from the first GuidAttribute on the assembly, 
        /// the string might be null or an invalid guid representation. False, 
        /// if there is no GuidAttribute with string argument.
        /// </summary>
        internal virtual bool GetGuidString(out string guidString)
        {
            return GetGuidStringDefaultImplementation(out guidString);
        }

        /// <summary>
        /// Gets the set of type identifiers from this assembly.
        /// </summary>
        /// <remarks>
        /// These names are the simple identifiers for the type, and do not include namespaces,
        /// outer type names, or type parameters.
        /// 
        /// This functionality can be used for features that want to quickly know if a name could be
        /// a type for performance reasons.  For example, classification does not want to incur an
        /// expensive binding call cost if it knows that there is no type with the name that they
        /// are looking at.
        /// </remarks>
        public abstract ICollection<string> TypeNames { get; }

        /// <summary>
        /// Gets the set of namespace names from this assembly.
        /// </summary>
        public abstract ICollection<string> NamespaceNames { get; }

        /// <summary>
        /// Returns true if this assembly might contain extension methods. If this property
        /// returns false, there are no extension methods in this assembly.
        /// </summary>
        /// <remarks>
        /// This property allows the search for extension methods to be narrowed quickly.
        /// </remarks>
        public abstract bool MightContainExtensionMethods { get; }

        /// <summary>
        /// Gets the symbol for the pre-defined type from core library associated with this assembly.
        /// </summary>
        /// <returns>The symbol for the pre-defined type or an error type if the type is not defined in the core library.</returns>
        internal NamedTypeSymbol GetSpecialType(SpecialType type)
        {
            return CorLibrary.GetDeclaredSpecialType(type);
        }

        internal static TypeSymbol DynamicType
        {
            get
            {
                return DynamicTypeSymbol.Instance;
            }
        }

        /// <summary>
        /// The NamedTypeSymbol for the .NET System.Object type, which could have a TypeKind of
        /// Error if there was no COR Library in a compilation using the assembly.
        /// </summary>
        internal NamedTypeSymbol ObjectType
        {
            get
            {
                return GetSpecialType(SpecialType.System_Object);
            }
        }

        /// <summary>
        /// Get symbol for predefined type from Cor Library used by this assembly.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal NamedTypeSymbol GetPrimitiveType(Microsoft.Cci.PrimitiveTypeCode type)
        {
            return GetSpecialType(SpecialTypes.GetTypeFromMetadataName(type));
        }

        /// <summary>
        /// Lookup a type within the assembly using the canonical CLR metadata name of the type.
        /// </summary>
        /// <param name="fullyQualifiedMetadataName">Type name.</param>
        /// <returns>Symbol for the type or null if type cannot be found or is ambiguous. </returns>
        public NamedTypeSymbol GetTypeByMetadataName(string fullyQualifiedMetadataName)
        {
            if (fullyQualifiedMetadataName == null)
            {
                throw new ArgumentNullException(nameof(fullyQualifiedMetadataName));
            }

            return this.GetTypeByMetadataName(fullyQualifiedMetadataName, includeReferences: false, isWellKnownType: false, conflicts: out var _);
        }

        /// <summary>
        /// Lookup a type within the assembly using its canonical CLR metadata name.
        /// </summary>
        /// <param name="metadataName"></param>
        /// <param name="includeReferences">
        /// If search within assembly fails, lookup in assemblies referenced by the primary module.
        /// For source assembly, this is equivalent to all assembly references given to compilation.
        /// </param>
        /// <param name="isWellKnownType">
        /// Extra restrictions apply when searching for a well-known type.  In particular, the type must be public.
        /// </param>
        /// <param name="useCLSCompliantNameArityEncoding">
        /// While resolving the name, consider only types following CLS-compliant generic type names and arity encoding (ECMA-335, section 10.7.2).
        /// I.e. arity is inferred from the name and matching type must have the same emitted name and arity.
        /// </param>
        /// <param name="warnings">
        /// A diagnostic bag to receive warnings if we should allow multiple definitions and pick one.
        /// </param>
        /// <param name="ignoreCorLibraryDuplicatedTypes">
        /// In case duplicate types are found, ignore the one from corlib. This is useful for any kind of compilation at runtime
        /// (EE/scripting/Powershell) using a type that is being migrated to corlib.
        /// </param>
        /// <param name="conflicts">
        /// In cases a type could not be found because of ambiguity, we return two of the candidates that caused the ambiguity.
        /// </param>
        /// <returns>Null if the type can't be found.</returns>
        internal NamedTypeSymbol GetTypeByMetadataName(
            string metadataName,
            bool includeReferences,
            bool isWellKnownType,
            out (AssemblySymbol, AssemblySymbol) conflicts,
            bool useCLSCompliantNameArityEncoding = false,
            DiagnosticBag warnings = null,
            bool ignoreCorLibraryDuplicatedTypes = false)
        {
            NamedTypeSymbol type;
            MetadataTypeName mdName;

            if (metadataName.IndexOf('+') >= 0)
            {
                var parts = metadataName.Split(s_nestedTypeNameSeparators);
                Debug.Assert(parts.Length > 0);
                mdName = MetadataTypeName.FromFullName(parts[0], useCLSCompliantNameArityEncoding);
                type = GetTopLevelTypeByMetadataName(ref mdName, assemblyOpt: null, includeReferences: includeReferences, isWellKnownType: isWellKnownType,
                    conflicts: out conflicts, warnings: warnings, ignoreCorLibraryDuplicatedTypes: ignoreCorLibraryDuplicatedTypes);

                for (int i = 1; (object)type != null && !type.IsErrorType() && i < parts.Length; i++)
                {
                    mdName = MetadataTypeName.FromTypeName(parts[i]);
                    NamedTypeSymbol temp = type.LookupMetadataType(ref mdName);
                    type = (!isWellKnownType || IsValidWellKnownType(temp)) ? temp : null;
                }
            }
            else
            {
                mdName = MetadataTypeName.FromFullName(metadataName, useCLSCompliantNameArityEncoding);
                type = GetTopLevelTypeByMetadataName(ref mdName, assemblyOpt: null, includeReferences: includeReferences, isWellKnownType: isWellKnownType,
                    conflicts: out conflicts, warnings: warnings, ignoreCorLibraryDuplicatedTypes: ignoreCorLibraryDuplicatedTypes);
            }

            return ((object)type == null || type.IsErrorType()) ? null : type;
        }

        private static readonly char[] s_nestedTypeNameSeparators = new char[] { '+' };

        /// <summary>
        /// Resolves <see cref="System.Type"/> to a <see cref="TypeSymbol"/> available in this assembly
        /// its referenced assemblies.
        /// </summary>
        /// <param name="type">The type to resolve.</param>
        /// <param name="includeReferences">Use referenced assemblies for resolution.</param>
        /// <returns>The resolved symbol if successful or null on failure.</returns>
        internal TypeSymbol GetTypeByReflectionType(Type type, bool includeReferences)
        {
            System.Reflection.TypeInfo typeInfo = type.GetTypeInfo();

            Debug.Assert(!typeInfo.IsByRef);

            // not supported (we don't accept open types as submission results nor host types):
            Debug.Assert(!typeInfo.ContainsGenericParameters);

            if (typeInfo.IsArray)
            {
                TypeSymbol symbol = GetTypeByReflectionType(typeInfo.GetElementType(), includeReferences);
                if ((object)symbol == null)
                {
                    return null;
                }

                int rank = typeInfo.GetArrayRank();

                return ArrayTypeSymbol.CreateCSharpArray(this, TypeWithAnnotations.Create(symbol), rank);
            }
            else if (typeInfo.IsPointer)
            {
                TypeSymbol symbol = GetTypeByReflectionType(typeInfo.GetElementType(), includeReferences);
                if ((object)symbol == null)
                {
                    return null;
                }

                return new PointerTypeSymbol(TypeWithAnnotations.Create(symbol));
            }
            else if (typeInfo.DeclaringType != null)
            {
                Debug.Assert(!typeInfo.IsArray);

                // consolidated generic arguments (includes arguments of all declaring types):
                Type[] genericArguments = typeInfo.GenericTypeArguments;
                int typeArgumentIndex = 0;

                var currentTypeInfo = typeInfo.IsGenericType ? typeInfo.GetGenericTypeDefinition().GetTypeInfo() : typeInfo;
                var nestedTypes = ArrayBuilder<System.Reflection.TypeInfo>.GetInstance();
                while (true)
                {
                    Debug.Assert(currentTypeInfo.IsGenericTypeDefinition || !currentTypeInfo.IsGenericType);

                    nestedTypes.Add(currentTypeInfo);
                    if (currentTypeInfo.DeclaringType == null)
                    {
                        break;
                    }

                    currentTypeInfo = currentTypeInfo.DeclaringType.GetTypeInfo();
                }

                int i = nestedTypes.Count - 1;
                var symbol = (NamedTypeSymbol)GetTypeByReflectionType(nestedTypes[i].AsType(), includeReferences);
                if ((object)symbol == null)
                {
                    return null;
                }

                while (--i >= 0)
                {
                    int forcedArity = nestedTypes[i].GenericTypeParameters.Length - nestedTypes[i + 1].GenericTypeParameters.Length;
                    MetadataTypeName mdName = MetadataTypeName.FromTypeName(nestedTypes[i].Name, forcedArity: forcedArity);

                    symbol = symbol.LookupMetadataType(ref mdName);
                    if ((object)symbol == null || symbol.IsErrorType())
                    {
                        return null;
                    }

                    symbol = ApplyGenericArguments(symbol, genericArguments, ref typeArgumentIndex, includeReferences);
                    if ((object)symbol == null)
                    {
                        return null;
                    }
                }

                nestedTypes.Free();
                Debug.Assert(typeArgumentIndex == genericArguments.Length);
                return symbol;
            }
            else
            {
                AssemblyIdentity assemblyId = AssemblyIdentity.FromAssemblyDefinition(typeInfo.Assembly);

                MetadataTypeName mdName = MetadataTypeName.FromNamespaceAndTypeName(
                    typeInfo.Namespace ?? string.Empty,
                    typeInfo.Name,
                    forcedArity: typeInfo.GenericTypeArguments.Length);

                NamedTypeSymbol symbol = GetTopLevelTypeByMetadataName(ref mdName, assemblyId, includeReferences, isWellKnownType: false, conflicts: out var _);

                if ((object)symbol == null || symbol.IsErrorType())
                {
                    return null;
                }

                int typeArgumentIndex = 0;
                Type[] genericArguments = typeInfo.GenericTypeArguments;
                symbol = ApplyGenericArguments(symbol, genericArguments, ref typeArgumentIndex, includeReferences);
                Debug.Assert(typeArgumentIndex == genericArguments.Length);
                return symbol;
            }
        }

        private NamedTypeSymbol ApplyGenericArguments(NamedTypeSymbol symbol, Type[] typeArguments, ref int currentTypeArgument, bool includeReferences)
        {
            int remainingTypeArguments = typeArguments.Length - currentTypeArgument;

            // in case we are specializing a nested generic definition we might have more arguments than the current symbol:
            Debug.Assert(remainingTypeArguments >= symbol.Arity);

            if (remainingTypeArguments == 0)
            {
                return symbol;
            }

            var length = symbol.TypeArgumentsWithAnnotationsNoUseSiteDiagnostics.Length;
            var typeArgumentSymbols = ArrayBuilder<TypeWithAnnotations>.GetInstance(length);
            for (int i = 0; i < length; i++)
            {
                var argSymbol = GetTypeByReflectionType(typeArguments[currentTypeArgument++], includeReferences);
                if ((object)argSymbol == null)
                {
                    return null;
                }
                typeArgumentSymbols.Add(TypeWithAnnotations.Create(argSymbol));
            }

            return symbol.ConstructIfGeneric(typeArgumentSymbols.ToImmutableAndFree());
        }

        internal NamedTypeSymbol GetTopLevelTypeByMetadataName(
            ref MetadataTypeName metadataName,
            AssemblyIdentity assemblyOpt,
            bool includeReferences,
            bool isWellKnownType,
            out (AssemblySymbol, AssemblySymbol) conflicts,
            DiagnosticBag warnings = null, // this is set to collect ambiguity warning for well-known types before C# 7
            bool ignoreCorLibraryDuplicatedTypes = false)
        {
            // Type from this assembly always wins.
            // After that we look in references, which may yield ambiguities. If `ignoreCorLibraryDuplicatedTypes` is set,
            // corlib does not contribute to ambiguities (corlib loses over other references).
            // For well-known types before C# 7, ambiguities are reported as a warning and the first candidate wins.
            // For other types, when `ignoreCorLibraryDuplicatedTypes` isn't set, finding a candidate in corlib resolves
            // ambiguities (corlib wins over other references).

            Debug.Assert(warnings is null || isWellKnownType);

            conflicts = default;
            NamedTypeSymbol result;

            // First try this assembly
            result = GetTopLevelTypeByMetadataName(this, ref metadataName, assemblyOpt);

            if (isWellKnownType && !IsValidWellKnownType(result))
            {
                result = null;
            }

            // ignore any types of the same name that might be in referenced assemblies (prefer the current assembly):
            if ((object)result != null || !includeReferences)
            {
                return result;
            }

            // Then try corlib, when finding a result there means we've found the final result
            bool isWellKnownTypeBeforeCSharp7 = isWellKnownType && warnings is not null;
            bool skipCorLibrary = false;

            if (CorLibrary != (object)this &&
                !CorLibrary.IsMissing &&
                !isWellKnownTypeBeforeCSharp7 && !ignoreCorLibraryDuplicatedTypes)
            {
                NamedTypeSymbol corLibCandidate = GetTopLevelTypeByMetadataName(CorLibrary, ref metadataName, assemblyOpt);
                skipCorLibrary = true;

                if (isValidCandidate(corLibCandidate, isWellKnownType))
                {
                    return corLibCandidate;
                }
            }

            Debug.Assert(this is SourceAssemblySymbol,
                "Never include references for a non-source assembly, because they don't know about aliases.");

            var assemblies = ArrayBuilder<AssemblySymbol>.GetInstance();

            // ignore reference aliases if searching for a type from a specific assembly:
            if (assemblyOpt != null)
            {
                assemblies.AddRange(DeclaringCompilation.GetBoundReferenceManager().ReferencedAssemblies);
            }
            else
            {
                DeclaringCompilation.GetUnaliasedReferencedAssemblies(assemblies);
            }

            // Lookup in references
            foreach (var assembly in assemblies)
            {
                Debug.Assert(!(this is SourceAssemblySymbol && assembly.IsMissing)); // Non-source assemblies can have missing references

                if (skipCorLibrary && assembly == (object)CorLibrary)
                {
                    continue;
                }

                NamedTypeSymbol candidate = GetTopLevelTypeByMetadataName(assembly, ref metadataName, assemblyOpt);

                if (!isValidCandidate(candidate, isWellKnownType))
                {
                    continue;
                }

                Debug.Assert(!TypeSymbol.Equals(candidate, result, TypeCompareKind.ConsiderEverything));

                if ((object)result != null)
                {
                    // duplicate
                    if (ignoreCorLibraryDuplicatedTypes)
                    {
                        if (IsInCorLib(candidate))
                        {
                            // ignore candidate
                            continue;
                        }
                        if (IsInCorLib(result))
                        {
                            // drop previous result
                            result = candidate;
                            continue;
                        }
                    }

                    if (warnings is null)
                    {
                        conflicts = (result.ContainingAssembly, candidate.ContainingAssembly);
                        result = null;
                    }
                    else
                    {
                        // The predefined type '{0}' is defined in multiple assemblies in the global alias; using definition from '{1}'
                        warnings.Add(ErrorCode.WRN_MultiplePredefTypes, NoLocation.Singleton, result, result.ContainingAssembly);
                    }

                    break;
                }

                result = candidate;
            }

            assemblies.Free();
            return result;

            bool isValidCandidate(NamedTypeSymbol candidate, bool isWellKnownType)
            {
                return candidate is not null
                    && (!isWellKnownType || IsValidWellKnownType(candidate))
                    && !candidate.IsHiddenByCodeAnalysisEmbeddedAttribute();
            }
        }

        private bool IsInCorLib(NamedTypeSymbol type)
        {
            return (object)type.ContainingAssembly == CorLibrary;
        }

        private bool IsValidWellKnownType(NamedTypeSymbol result)
        {
            if ((object)result == null || result.TypeKind == TypeKind.Error)
            {
                return false;
            }

            Debug.Assert((object)result.ContainingType == null || IsValidWellKnownType(result.ContainingType),
                "Checking the containing type is the caller's responsibility.");

            return result.DeclaredAccessibility == Accessibility.Public || IsSymbolAccessible(result, this);
        }

        private static NamedTypeSymbol GetTopLevelTypeByMetadataName(AssemblySymbol assembly, ref MetadataTypeName metadataName, AssemblyIdentity assemblyOpt)
        {
            var result = assembly.LookupTopLevelMetadataType(ref metadataName, digThroughForwardedTypes: false);
            if (!IsAcceptableMatchForGetTypeByMetadataName(result))
            {
                return null;
            }

            if (assemblyOpt != null && !assemblyOpt.Equals(assembly.Identity))
            {
                return null;
            }

            return result;
        }

        private static bool IsAcceptableMatchForGetTypeByMetadataName(NamedTypeSymbol candidate)
        {
            return candidate.Kind != SymbolKind.ErrorType || !(candidate is MissingMetadataTypeSymbol);
        }

        /// <summary>
        /// Lookup member declaration in predefined CorLib type in this Assembly. Only valid if this 
        /// assembly is the Cor Library
        /// </summary>
        internal virtual Symbol GetDeclaredSpecialTypeMember(SpecialMember member)
        {
            return null;
        }

        /// <summary>
        /// Lookup member declaration in predefined CorLib type used by this Assembly.
        /// </summary>
        internal virtual Symbol GetSpecialTypeMember(SpecialMember member)
        {
            return CorLibrary.GetDeclaredSpecialTypeMember(member);
        }

        internal abstract ImmutableArray<byte> PublicKey { get; }

        /// <summary>
        /// If this symbol represents a metadata assembly returns the underlying <see cref="AssemblyMetadata"/>.
        /// 
        /// Otherwise, this returns <see langword="null"/>.
        /// </summary>
        public abstract AssemblyMetadata GetMetadata();

        protected override ISymbol CreateISymbol()
        {
            return new PublicModel.NonSourceAssemblySymbol(this);
        }
    }
}
