// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Compilation.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Razor.Runtime.TagHelpers
{
    /// <summary>
    /// Factory for <see cref="TagHelperDescriptor"/>s from <see cref="Type"/>s.
    /// </summary>
    public class TagHelperDescriptorFactory
    {
        private const string DataDashPrefix = "data-";
        private const string TagHelperNameEnding = "TagHelper";
        private const string HtmlCaseRegexReplacement = "-$1$2";
        private const char RequiredAttributeWildcardSuffix = '*';

        // This matches the following AFTER the start of the input string (MATCH).
        // Any letter/number followed by an uppercase letter then lowercase letter: 1(Aa), a(Aa), A(Aa)
        // Any lowercase letter followed by an uppercase letter: a(A)
        // Each match is then prefixed by a "-" via the ToHtmlCase method.
        private static readonly Regex HtmlCaseRegex =
            new Regex(
                "(?<!^)((?<=[a-zA-Z0-9])[A-Z][a-z])|((?<=[a-z])[A-Z])",
                RegexOptions.None,
                Constants.RegexMatchTimeout);

#if !NETSTANDARD1_3
        private readonly TagHelperDesignTimeDescriptorFactory _designTimeDescriptorFactory;
#endif
        private readonly bool _designTime;

        // TODO: Investigate if we should cache TagHelperDescriptors for types:
        // https://github.com/aspnet/Razor/issues/165

        public static ICollection<char> InvalidNonWhitespaceNameCharacters { get; } = new HashSet<char>(
            new[] { '@', '!', '<', '/', '?', '[', '>', ']', '=', '"', '\'', '*' });

        /// <summary>
        /// Instantiates a new <see cref="TagHelperDescriptorFactory"/>.
        /// </summary>
        /// <param name="designTime">
        /// Indicates if <see cref="TagHelperDescriptor"/>s should be created for design time.
        /// </param>
        public TagHelperDescriptorFactory(bool designTime)
        {
#if !NETSTANDARD1_3
            if (designTime)
            {
                _designTimeDescriptorFactory = new TagHelperDesignTimeDescriptorFactory();
            }
#endif

            _designTime = designTime;
        }

        /// <summary>
        /// Creates a <see cref="TagHelperDescriptor"/> from the given <paramref name="type"/>.
        /// </summary>
        /// <param name="assemblyName">The assembly name that contains <paramref name="type"/>.</param>
        /// <param name="type">The <see cref="Type"/> to create a <see cref="TagHelperDescriptor"/> from.
        /// </param>
        /// <param name="errorSink">The <see cref="ErrorSink"/> used to collect <see cref="RazorError"/>s encountered
        /// when creating <see cref="TagHelperDescriptor"/>s for the given <paramref name="type"/>.</param>
        /// <returns>
        /// A collection of <see cref="TagHelperDescriptor"/>s that describe the given <paramref name="type"/>.
        /// </returns>
        public virtual IEnumerable<TagHelperDescriptor> CreateDescriptors(
            string assemblyName,
            Type type,
            ErrorSink errorSink)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (errorSink == null)
            {
                throw new ArgumentNullException(nameof(errorSink));
            }

            if (ShouldSkipDescriptorCreation(type.GetTypeInfo()))
            {
                return Enumerable.Empty<TagHelperDescriptor>();
            }

            var attributeDescriptors = GetAttributeDescriptors(type, errorSink);
            var targetElementAttributes = GetValidHtmlTargetElementAttributes(type, errorSink);
            var allowedChildren = GetAllowedChildren(type, errorSink);

            var tagHelperDescriptors =
                BuildTagHelperDescriptors(
                    type,
                    assemblyName,
                    attributeDescriptors,
                    targetElementAttributes,
                    allowedChildren);

            return tagHelperDescriptors.Distinct(TagHelperDescriptorComparer.Default);
        }

        private static IEnumerable<HtmlTargetElementAttribute> GetValidHtmlTargetElementAttributes(
            Type type,
            ErrorSink errorSink)
        {
            var targetElementAttributes = type
                .GetTypeInfo()
                .GetCustomAttributes<HtmlTargetElementAttribute>(inherit: false);
            return targetElementAttributes.Where(
                attribute => ValidHtmlTargetElementAttributeNames(attribute, errorSink));
        }

        private IEnumerable<TagHelperDescriptor> BuildTagHelperDescriptors(
            Type type,
            string assemblyName,
            IEnumerable<TagHelperAttributeDescriptor> attributeDescriptors,
            IEnumerable<HtmlTargetElementAttribute> targetElementAttributes,
            IEnumerable<string> allowedChildren)
        {
            TagHelperDesignTimeDescriptor typeDesignTimeDescriptor = null;

#if !NETSTANDARD1_3
            if (_designTime)
            {
                typeDesignTimeDescriptor = _designTimeDescriptorFactory.CreateDescriptor(type);
            }
#endif

            var typeName = type.FullName;

            // If there isn't an attribute specifying the tag name derive it from the name
            if (!targetElementAttributes.Any())
            {
                var name = type.Name;

                if (name.EndsWith(TagHelperNameEnding, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - TagHelperNameEnding.Length);
                }

                return new[]
                {
                    BuildTagHelperDescriptor(
                        ToHtmlCase(name),
                        typeName,
                        assemblyName,
                        attributeDescriptors,
                        requiredAttributeDescriptors: Enumerable.Empty<TagHelperRequiredAttributeDescriptor>(),
                        allowedChildren: allowedChildren,
                        tagStructure: default(TagStructure),
                        parentTag: null,
                        designTimeDescriptor: typeDesignTimeDescriptor)
                };
            }

            return targetElementAttributes.Select(
                attribute =>
                    BuildTagHelperDescriptor(
                        typeName,
                        assemblyName,
                        attributeDescriptors,
                        attribute,
                        allowedChildren,
                        typeDesignTimeDescriptor));
        }

        private static IEnumerable<string> GetAllowedChildren(Type type, ErrorSink errorSink)
        {
            var restrictChildrenAttribute = type.GetTypeInfo().GetCustomAttribute<RestrictChildrenAttribute>(inherit: false);
            if (restrictChildrenAttribute == null)
            {
                return null;
            }

            var allowedChildren = restrictChildrenAttribute.ChildTags;
            var validAllowedChildren = GetValidAllowedChildren(allowedChildren, type.FullName, errorSink);

            if (validAllowedChildren.Any())
            {
                return validAllowedChildren;
            }
            else
            {
                // All allowed children were invalid, return null to indicate that any child is acceptable.
                return null;
            }
        }

        // Internal for unit testing
        internal static IEnumerable<string> GetValidAllowedChildren(
            IEnumerable<string> allowedChildren,
            string tagHelperName,
            ErrorSink errorSink)
        {
            var validAllowedChildren = new List<string>();

            foreach (var name in allowedChildren)
            {
                var valid = TryValidateName(
                    name,
                    whitespaceError:
                        Resources.FormatTagHelperDescriptorFactory_InvalidRestrictChildrenAttributeNameNullWhitespace(
                            nameof(RestrictChildrenAttribute),
                            tagHelperName),
                    characterErrorBuilder: (invalidCharacter) =>
                        Resources.FormatTagHelperDescriptorFactory_InvalidRestrictChildrenAttributeName(
                            nameof(RestrictChildrenAttribute),
                            name,
                            tagHelperName,
                            invalidCharacter),
                    errorSink: errorSink);

                if (valid)
                {
                    validAllowedChildren.Add(name);
                }
            }

            return validAllowedChildren;
        }

        private static TagHelperDescriptor BuildTagHelperDescriptor(
            string typeName,
            string assemblyName,
            IEnumerable<TagHelperAttributeDescriptor> attributeDescriptors,
            HtmlTargetElementAttribute targetElementAttribute,
            IEnumerable<string> allowedChildren,
            TagHelperDesignTimeDescriptor designTimeDescriptor)
        {
            IEnumerable<TagHelperRequiredAttributeDescriptor> requiredAttributeDescriptors;
            TryGetRequiredAttributeDescriptors(targetElementAttribute.Attributes, errorSink: null, descriptors: out requiredAttributeDescriptors);

            return BuildTagHelperDescriptor(
                targetElementAttribute.Tag,
                typeName,
                assemblyName,
                attributeDescriptors,
                requiredAttributeDescriptors,
                allowedChildren,
                targetElementAttribute.ParentTag,
                targetElementAttribute.TagStructure,
                designTimeDescriptor);
        }

        private static TagHelperDescriptor BuildTagHelperDescriptor(
            string tagName,
            string typeName,
            string assemblyName,
            IEnumerable<TagHelperAttributeDescriptor> attributeDescriptors,
            IEnumerable<TagHelperRequiredAttributeDescriptor> requiredAttributeDescriptors,
            IEnumerable<string> allowedChildren,
            string parentTag,
            TagStructure tagStructure,
            TagHelperDesignTimeDescriptor designTimeDescriptor)
        {
            return new TagHelperDescriptor
            {
                TagName = tagName,
                TypeName = typeName,
                AssemblyName = assemblyName,
                Attributes = attributeDescriptors,
                RequiredAttributes = requiredAttributeDescriptors,
                AllowedChildren = allowedChildren,
                RequiredParent = parentTag,
                TagStructure = tagStructure,
                DesignTimeDescriptor = designTimeDescriptor
            };
        }

        /// <summary>
        /// Internal for testing.
        /// </summary>
        internal static bool ValidHtmlTargetElementAttributeNames(
            HtmlTargetElementAttribute attribute,
            ErrorSink errorSink)
        {
            var validTagName = ValidateName(attribute.Tag, targetingAttributes: false, errorSink: errorSink);
            IEnumerable<TagHelperRequiredAttributeDescriptor> requiredAttributeDescriptors;
            var validRequiredAttributes = TryGetRequiredAttributeDescriptors(attribute.Attributes, errorSink, out requiredAttributeDescriptors);
            var validParentTagName = ValidateParentTagName(attribute.ParentTag, errorSink);

            return validTagName && validRequiredAttributes && validParentTagName;
        }

        /// <summary>
        /// Internal for unit testing.
        /// </summary>
        internal static bool ValidateParentTagName(string parentTag, ErrorSink errorSink)
        {
            return parentTag == null ||
                TryValidateName(
                    parentTag,
                    Resources.FormatHtmlTargetElementAttribute_NameCannotBeNullOrWhitespace(
                        Resources.TagHelperDescriptorFactory_ParentTag),
                    characterErrorBuilder: (invalidCharacter) =>
                        Resources.FormatHtmlTargetElementAttribute_InvalidName(
                            Resources.TagHelperDescriptorFactory_ParentTag.ToLower(),
                            parentTag,
                            invalidCharacter),
                    errorSink: errorSink);
        }

        private static bool TryGetRequiredAttributeDescriptors(
            string requiredAttributes,
            ErrorSink errorSink,
            out IEnumerable<TagHelperRequiredAttributeDescriptor> descriptors)
        {
            var parser = new RequiredAttributeParser(requiredAttributes);

            return parser.TryParse(errorSink, out descriptors);
        }

        private static bool ValidateName(string name, bool targetingAttributes, ErrorSink errorSink)
        {
            if (!targetingAttributes &&
                string.Equals(
                    name,
                    TagHelperDescriptorProvider.ElementCatchAllTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                // '*' as the entire name is OK in the HtmlTargetElement catch-all case.
                return true;
            }

            var targetName = targetingAttributes ?
                Resources.TagHelperDescriptorFactory_Attribute :
                Resources.TagHelperDescriptorFactory_Tag;

            var validName = TryValidateName(
                name,
                whitespaceError: Resources.FormatHtmlTargetElementAttribute_NameCannotBeNullOrWhitespace(targetName),
                characterErrorBuilder: (invalidCharacter) =>
                    Resources.FormatHtmlTargetElementAttribute_InvalidName(
                        targetName.ToLower(),
                        name,
                        invalidCharacter),
                errorSink: errorSink);

            return validName;
        }

        private static bool TryValidateName(
            string name,
            string whitespaceError,
            Func<char, string> characterErrorBuilder,
            ErrorSink errorSink)
        {
            var validName = true;

            if (string.IsNullOrWhiteSpace(name))
            {
                errorSink.OnError(SourceLocation.Zero, whitespaceError, length: 0);

                validName = false;
            }
            else
            {
                foreach (var character in name)
                {
                    if (char.IsWhiteSpace(character) ||
                        InvalidNonWhitespaceNameCharacters.Contains(character))
                    {
                        var error = characterErrorBuilder(character);
                        errorSink.OnError(SourceLocation.Zero, error, length: 0);

                        validName = false;
                    }
                }
            }

            return validName;
        }

        private IEnumerable<TagHelperAttributeDescriptor> GetAttributeDescriptors(Type type, ErrorSink errorSink)
        {
            var attributeDescriptors = new List<TagHelperAttributeDescriptor>();

            // Keep indexer descriptors separate to avoid sorting the combined list later.
            var indexerDescriptors = new List<TagHelperAttributeDescriptor>();

            var accessibleProperties = type.GetRuntimeProperties().Where(IsAccessibleProperty);
            foreach (var property in accessibleProperties)
            {
                if (ShouldSkipDescriptorCreation(property))
                {
                    continue;
                }

                var attributeNameAttribute = property
                    .GetCustomAttributes<HtmlAttributeNameAttribute>(inherit: false)
                    .FirstOrDefault();
                var hasExplicitName =
                    attributeNameAttribute != null && !string.IsNullOrEmpty(attributeNameAttribute.Name);
                var attributeName = hasExplicitName ? attributeNameAttribute.Name : ToHtmlCase(property.Name);

                TagHelperAttributeDescriptor mainDescriptor = null;
                if (property.SetMethod != null && property.SetMethod.IsPublic)
                {
                    mainDescriptor = ToAttributeDescriptor(property, attributeName);
                    if (!ValidateTagHelperAttributeDescriptor(mainDescriptor, type, errorSink))
                    {
                        // HtmlAttributeNameAttribute.Name is invalid. Ignore this property completely.
                        continue;
                    }
                }
                else if (hasExplicitName)
                {
                    // Specified HtmlAttributeNameAttribute.Name though property has no public setter.
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameNotNullOrEmpty(
                            type.FullName,
                            property.Name,
                            typeof(HtmlAttributeNameAttribute).FullName,
                            nameof(HtmlAttributeNameAttribute.Name)),
                        length: 0);
                    continue;
                }

                bool isInvalid;
                var indexerDescriptor = ToIndexerAttributeDescriptor(
                    property,
                    attributeNameAttribute,
                    parentType: type,
                    errorSink: errorSink,
                    defaultPrefix: attributeName + "-",
                    isInvalid: out isInvalid);
                if (indexerDescriptor != null &&
                    !ValidateTagHelperAttributeDescriptor(indexerDescriptor, type, errorSink))
                {
                    isInvalid = true;
                }

                if (isInvalid)
                {
                    // The property type or HtmlAttributeNameAttribute.DictionaryAttributePrefix (or perhaps the
                    // HTML-casing of the property name) is invalid. Ignore this property completely.
                    continue;
                }

                if (mainDescriptor != null)
                {
                    attributeDescriptors.Add(mainDescriptor);
                }

                if (indexerDescriptor != null)
                {
                    indexerDescriptors.Add(indexerDescriptor);
                }
            }

            attributeDescriptors.AddRange(indexerDescriptors);

            return attributeDescriptors;
        }

        // Internal for testing.
        internal static bool ValidateTagHelperAttributeDescriptor(
            TagHelperAttributeDescriptor attributeDescriptor,
            Type parentType,
            ErrorSink errorSink)
        {
            string nameOrPrefix;
            if (attributeDescriptor.IsIndexer)
            {
                nameOrPrefix = Resources.TagHelperDescriptorFactory_Prefix;
            }
            else if (string.IsNullOrEmpty(attributeDescriptor.Name))
            {
                errorSink.OnError(
                    SourceLocation.Zero,
                    Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameNullOrEmpty(
                        parentType.FullName,
                        attributeDescriptor.PropertyName),
                    length: 0);

                return false;
            }
            else
            {
                nameOrPrefix = Resources.TagHelperDescriptorFactory_Name;
            }

            return ValidateTagHelperAttributeNameOrPrefix(
                attributeDescriptor.Name,
                parentType,
                attributeDescriptor.PropertyName,
                errorSink,
                nameOrPrefix);
        }

        private bool ShouldSkipDescriptorCreation(MemberInfo memberInfo)
        {
            if (_designTime)
            {
                var editorBrowsableAttribute = memberInfo.GetCustomAttribute<EditorBrowsableAttribute>(inherit: false);

                return editorBrowsableAttribute != null &&
                    editorBrowsableAttribute.State == EditorBrowsableState.Never;
            }

            return false;
        }

        private static bool ValidateTagHelperAttributeNameOrPrefix(
            string attributeNameOrPrefix,
            Type parentType,
            string propertyName,
            ErrorSink errorSink,
            string nameOrPrefix)
        {
            if (string.IsNullOrEmpty(attributeNameOrPrefix))
            {
                // ValidateTagHelperAttributeDescriptor validates Name is non-null and non-empty. The empty string is
                // valid for DictionaryAttributePrefix and null is impossible at this point because it means "don't
                // create a descriptor". (Empty DictionaryAttributePrefix is a corner case which would bind every
                // attribute of a target element. Likely not particularly useful but unclear what minimum length
                // should be required and what scenarios a minimum length would break.)
                return true;
            }

            if (string.IsNullOrWhiteSpace(attributeNameOrPrefix))
            {
                // Provide a single error if the entire name is whitespace, not an error per character.
                errorSink.OnError(
                    SourceLocation.Zero,
                    Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameOrPrefixWhitespace(
                        parentType.FullName,
                        propertyName,
                        nameOrPrefix),
                    length: 0);

                return false;
            }

            // data-* attributes are explicitly not implemented by user agents and are not intended for use on
            // the server; therefore it's invalid for TagHelpers to bind to them.
            if (attributeNameOrPrefix.StartsWith(DataDashPrefix, StringComparison.OrdinalIgnoreCase))
            {
                errorSink.OnError(
                    SourceLocation.Zero,
                    Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameOrPrefixStart(
                        parentType.FullName,
                        propertyName,
                        nameOrPrefix,
                        attributeNameOrPrefix,
                        DataDashPrefix),
                    length: 0);

                return false;
            }

            var isValid = true;
            foreach (var character in attributeNameOrPrefix)
            {
                if (char.IsWhiteSpace(character) || InvalidNonWhitespaceNameCharacters.Contains(character))
                {
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameOrPrefixCharacter(
                            parentType.FullName,
                            propertyName,
                            nameOrPrefix,
                            attributeNameOrPrefix,
                            character),
                    length: 0);

                    isValid = false;
                }
            }

            return isValid;
        }

        private TagHelperAttributeDescriptor ToAttributeDescriptor(PropertyInfo property, string attributeName)
        {
            return ToAttributeDescriptor(
                property,
                attributeName,
                property.PropertyType.FullName,
                isIndexer: false,
                isStringProperty: typeof(string) == property.PropertyType);
        }

        private TagHelperAttributeDescriptor ToIndexerAttributeDescriptor(
            PropertyInfo property,
            HtmlAttributeNameAttribute attributeNameAttribute,
            Type parentType,
            ErrorSink errorSink,
            string defaultPrefix,
            out bool isInvalid)
        {
            isInvalid = false;
            var hasPublicSetter = property.SetMethod != null && property.SetMethod.IsPublic;
            var dictionaryTypeArguments = ClosedGenericMatcher.ExtractGenericInterface(
                property.PropertyType,
                typeof(IDictionary<,>))
                ?.GenericTypeArguments
                .Select(type => type.IsGenericParameter ? null : type)
                .ToArray();
            if (dictionaryTypeArguments?[0] != typeof(string))
            {
                if (attributeNameAttribute?.DictionaryAttributePrefix != null)
                {
                    // DictionaryAttributePrefix is not supported unless associated with an
                    // IDictionary<string, TValue> property.
                    isInvalid = true;
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.FormatTagHelperDescriptorFactory_InvalidAttributePrefixNotNull(
                            parentType.FullName,
                            property.Name,
                            nameof(HtmlAttributeNameAttribute),
                            nameof(HtmlAttributeNameAttribute.DictionaryAttributePrefix),
                            "IDictionary<string, TValue>"),
                        length: 0);
                }
                else if (attributeNameAttribute != null && !hasPublicSetter)
                {
                    // Associated an HtmlAttributeNameAttribute with a non-dictionary property that lacks a public
                    // setter.
                    isInvalid = true;
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.FormatTagHelperDescriptorFactory_InvalidAttributeNameAttribute(
                            parentType.FullName,
                            property.Name,
                            nameof(HtmlAttributeNameAttribute),
                            "IDictionary<string, TValue>"),
                        length: 0);
                }

                return null;
            }
            else if (!hasPublicSetter &&
                attributeNameAttribute != null &&
                !attributeNameAttribute.DictionaryAttributePrefixSet)
            {
                // Must set DictionaryAttributePrefix when using HtmlAttributeNameAttribute with a dictionary property
                // that lacks a public setter.
                isInvalid = true;
                errorSink.OnError(
                    SourceLocation.Zero,
                    Resources.FormatTagHelperDescriptorFactory_InvalidAttributePrefixNull(
                        parentType.FullName,
                        property.Name,
                        nameof(HtmlAttributeNameAttribute),
                        nameof(HtmlAttributeNameAttribute.DictionaryAttributePrefix),
                        "IDictionary<string, TValue>"),
                    length: 0);

                return null;
            }

            // Potential prefix case. Use default prefix (based on name)?
            var useDefault = attributeNameAttribute == null || !attributeNameAttribute.DictionaryAttributePrefixSet;

            var prefix = useDefault ? defaultPrefix : attributeNameAttribute.DictionaryAttributePrefix;
            if (prefix == null)
            {
                // DictionaryAttributePrefix explicitly set to null. Ignore.
                return null;
            }

            return ToAttributeDescriptor(
                property,
                attributeName: prefix,
                typeName: dictionaryTypeArguments[1].FullName,
                isIndexer: true,
                isStringProperty: typeof(string) == dictionaryTypeArguments[1]);
        }

        private TagHelperAttributeDescriptor ToAttributeDescriptor(
            PropertyInfo property,
            string attributeName,
            string typeName,
            bool isIndexer,
            bool isStringProperty)
        {
            TagHelperAttributeDesignTimeDescriptor propertyDesignTimeDescriptor = null;

#if !NETSTANDARD1_3
            if (_designTime)
            {
                propertyDesignTimeDescriptor = _designTimeDescriptorFactory.CreateAttributeDescriptor(property);
            }
#endif

            return new TagHelperAttributeDescriptor
            {
                Name = attributeName,
                PropertyName = property.Name,
                IsEnum = property.PropertyType.GetTypeInfo().IsEnum,
                TypeName = typeName,
                IsStringProperty = isStringProperty,
                IsIndexer = isIndexer,
                DesignTimeDescriptor = propertyDesignTimeDescriptor
            };
        }

        private static bool IsAccessibleProperty(PropertyInfo property)
        {
            // Accessible properties are those with public getters and without [HtmlAttributeNotBound].
            return property.GetIndexParameters().Length == 0 &&
                property.GetMethod != null &&
                property.GetMethod.IsPublic &&
                property.GetCustomAttribute<HtmlAttributeNotBoundAttribute>(inherit: false) == null;
        }

        /// <summary>
        /// Converts from pascal/camel case to lower kebab-case.
        /// </summary>
        /// <example>
        /// SomeThing => some-thing
        /// capsONInside => caps-on-inside
        /// CAPSOnOUTSIDE => caps-on-outside
        /// ALLCAPS => allcaps
        /// One1Two2Three3 => one1-two2-three3
        /// ONE1TWO2THREE3 => one1two2three3
        /// First_Second_ThirdHi => first_second_third-hi
        /// </example>
        private static string ToHtmlCase(string name)
        {
            return HtmlCaseRegex.Replace(name, HtmlCaseRegexReplacement).ToLowerInvariant();
        }

        // Internal for testing
        internal class RequiredAttributeParser
        {
            private static readonly char[] InvalidPlainAttributeNameCharacters = { ' ', '\t', ',', RequiredAttributeWildcardSuffix };
            private static readonly char[] InvalidCSSAttributeNameCharacters = (new[] { ' ', '\t', ',', ']' }).Concat(TagHelperRequiredAttributeDescriptor.SupportedCSSValueOperators).ToArray();

            private int _index;
            private string _requiredAttributes;

            public RequiredAttributeParser(string requiredAttributes)
            {
                _requiredAttributes = requiredAttributes;
            }

            private char Current => _requiredAttributes[_index];

            private bool AtEnd => _index >= _requiredAttributes.Length;

            public bool TryParse(
                ErrorSink errorSink,
                out IEnumerable<TagHelperRequiredAttributeDescriptor> requiredAttributeDescriptors)
            {
                if (_requiredAttributes == null)
                {
                    requiredAttributeDescriptors = Enumerable.Empty<TagHelperRequiredAttributeDescriptor>();
                    return true;
                }

                requiredAttributeDescriptors = null;
                var descriptors = new List<TagHelperRequiredAttributeDescriptor>();

                while (!AtEnd)
                {
                    PassOptionalWhitespace();

                    TagHelperRequiredAttributeDescriptor descriptor;
                    if (At('['))
                    {
                        descriptor = ParseCSSSelector(errorSink);
                    }
                    else
                    {
                        descriptor = ParsePlainSelector(errorSink);
                    }

                    if (descriptor == null)
                    {
                        // Failed to create the descriptor due to an invalid required attribute.
                        return false;
                    }
                    else
                    {
                        descriptors.Add(descriptor);
                    }

                    PassOptionalWhitespace();

                    if (At(','))
                    {
                        Next();

                        if (AtEnd)
                        {
                            errorSink.OnError(
                                SourceLocation.Zero,
                                Resources.TagHelperDescriptorFactory_UnexpectedEndOfRequiredAttribute,
                                0);
                        }
                    }
                    else if (!AtEnd)
                    {
                        errorSink.OnError(
                            SourceLocation.Zero,
                            Resources.FormatTagHelperDescriptorFactory_InvalidRequiredAttributeCharacter(Current),
                            0);
                        return false;
                    }
                }

                requiredAttributeDescriptors = descriptors;
                return true;
            }

            private TagHelperRequiredAttributeDescriptor ParsePlainSelector(ErrorSink errorSink)
            {
                var nameEndIndex = _requiredAttributes.IndexOfAny(InvalidPlainAttributeNameCharacters, _index);
                string attributeName;

                var hasWildcard = false;
                if (nameEndIndex == -1)
                {
                    attributeName = _requiredAttributes.Substring(_index);
                    _index = _requiredAttributes.Length;
                }
                else
                {
                    attributeName = _requiredAttributes.Substring(_index, nameEndIndex - _index);
                    _index = nameEndIndex;

                    hasWildcard = _requiredAttributes[nameEndIndex] == RequiredAttributeWildcardSuffix;
                    if (hasWildcard)
                    {
                        // Move past wild card
                        Next();
                    }
                }

                TagHelperRequiredAttributeDescriptor descriptor = null;
                if (ValidateName(attributeName, targetingAttributes: true, errorSink: errorSink))
                {
                    descriptor = new TagHelperRequiredAttributeDescriptor
                    {
                        Name = attributeName
                    };

                    if (hasWildcard)
                    {
                        descriptor.Operator = RequiredAttributeWildcardSuffix;
                    }
                }

                return descriptor;
            }

            private string ParseCSSAttributeName(ErrorSink errorSink)
            {
                var nameStartIndex = _index;
                var nameEndIndex = _requiredAttributes.IndexOfAny(new char[] { ' ', '\t', ']', '=', '^', '$' }, _index);
                _index = nameEndIndex != -1 ? nameEndIndex : _requiredAttributes.Length;

                PassOptionalWhitespace();

                var attributeNameLength = 0;
                if (At('='))
                {
                    attributeNameLength = nameEndIndex - nameStartIndex;
                }
                else if (At(']'))
                {
                    attributeNameLength = nameEndIndex - nameStartIndex;
                }
                else if (NextIs('=') && TagHelperRequiredAttributeDescriptor.SupportedCSSValueOperators.Contains(Current))
                {
                    Next();
                    attributeNameLength = nameEndIndex - nameStartIndex;
                }
                else
                {
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.FormatTagHelperDescriptorFactory_InvalidRequiredAttributeCharacterExpectedOther(Current, ']'),
                        0);
                    return null;
                }

                var attributeName = _requiredAttributes.Substring(nameStartIndex, attributeNameLength);

                return attributeName;
            }

            private char ParseCSSValueOperator(ErrorSink errorSink)
            {
                Debug.Assert(Current == '=');

                // Move past the '='
                Next();
                var potentialOperator = _requiredAttributes[_index - 2];
                if (TagHelperRequiredAttributeDescriptor.IsSupportedCSSValueOperator(potentialOperator))
                {
                    return potentialOperator;
                }

                return '=';
            }

            private string ParseCSSValue(ErrorSink errorSink)
            {
                PassOptionalWhitespace();

                int valueStart, valueEnd;
                if (At('\'') || At('"'))
                {
                    var quote = Current;

                    // Move past the quote
                    Next();

                    valueStart = _index;
                    while (!At(quote))
                    {
                        if (AtEnd)
                        {
                            errorSink.OnError(
                                SourceLocation.Zero,
                                Resources.FormatTagHelperDescriptorFactory_InvalidRequiredAttributeCharacterExpectedOther(Current, quote),
                                0);
                            return null;
                        }

                        Next();
                    }
                    valueEnd = _index;

                    // Move past the end quote;
                    Next();
                }
                else
                {
                    valueStart = _index;
                    while (!AtEnd && !char.IsWhiteSpace(Current) && !At(']'))
                    {
                        Next();
                    }
                    valueEnd = _index;
                }

                PassOptionalWhitespace();

                var value = _requiredAttributes.Substring(valueStart, valueEnd - valueStart);

                return value;
            }

            private TagHelperRequiredAttributeDescriptor ParseCSSSelector(ErrorSink errorSink)
            {
                Debug.Assert(Current == '[');

                // Move past '['.
                Next();
                PassOptionalWhitespace();

                var attributeName = ParseCSSAttributeName(errorSink);

                if (attributeName == null)
                {
                    // Couldn't parse attribute name.
                    return null;
                }

                if (!ValidateName(attributeName, targetingAttributes: true, errorSink: errorSink))
                {
                    return null;
                }

                if (!EnsureNotAtEnd(errorSink))
                {
                    return null;
                }

                var valueOperator = default(char);
                if (At('='))
                {
                    valueOperator = ParseCSSValueOperator(errorSink);
                }

                if (!EnsureNotAtEnd(errorSink))
                {
                    return null;
                }

                var value = ParseCSSValue(errorSink);

                if (value == null)
                {
                    // Couldn't parse value
                    return null;
                }

                if (At(']'))
                {
                    // Move past the ending bracket.
                    Next();
                }
                else
                {
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.TagHelperDescriptorFactory_CouldNotFindMatchingEndBrace,
                        0);
                    return null;
                }

                return new TagHelperRequiredAttributeDescriptor
                {
                    Name = attributeName,
                    Value = value,
                    Operator = valueOperator,
                    IsCSSSelector = true,
                };
            }

            private bool EnsureNotAtEnd(ErrorSink errorSink)
            {
                if (AtEnd)
                {
                    errorSink.OnError(
                        SourceLocation.Zero,
                        Resources.TagHelperDescriptorFactory_CouldNotFindMatchingEndBrace,
                        0);

                    return false;
                }

                return true;
            }

            private void Next()
            {
                _index = Math.Min(_index + 1, _requiredAttributes.Length);
            }

            private bool NextIs(char c)
            {
                _index++;
                var nextIs = At(c);
                _index--;

                return nextIs;
            }

            private bool At(char c)
            {
                return !AtEnd && Current == c;
            }

            private void PassOptionalWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(Current))
                {
                    Next();
                }
            }
        }
    }
}