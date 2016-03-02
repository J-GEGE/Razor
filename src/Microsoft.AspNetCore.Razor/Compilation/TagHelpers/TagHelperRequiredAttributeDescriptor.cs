// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;

namespace Microsoft.AspNetCore.Razor.Compilation.TagHelpers
{
    /// <summary>
    /// A metadata class describing a required tag helper attribute.
    /// </summary>
    public class TagHelperRequiredAttributeDescriptor
    {
        /// <summary>
        /// Supported CSS value operators.
        /// </summary>
        public static readonly char[] SupportedCSSValueOperators = { '=', '^', '$' };

        /// <summary>
        /// The HTML attribute name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The HTML attribute selector value. If <see cref="IsCSSSelector"/> is not <c>true</c>, this field is
        /// ignored.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// An operator that modifies how a required attribute is applied to an HTML attribute value or name.
        /// </summary>
        public char Operator { get; set; }

        /// <summary>
        /// Indicates if the <see cref="TagHelperRequiredAttributeDescriptor"/> represents a CSS selector.
        /// </summary>
        public bool IsCSSSelector { get; set; }

        /// <summary>
        /// Determines if the current <see cref="TagHelperRequiredAttributeDescriptor"/> matches the given
        /// <paramref name="attributeName"/> and <paramref name="attributeValue"/>.
        /// </summary>
        /// <param name="attributeName">An HTML attribute name.</param>
        /// <param name="attributeValue">An HTML attribute value.</param>
        /// <returns></returns>
        public bool Matches(string attributeName, string attributeValue)
        {
            if (IsCSSSelector)
            {
                var nameMatches = string.Equals(Name, attributeName, StringComparison.OrdinalIgnoreCase);

                if (!nameMatches)
                {
                    return false;
                }

                var valueMatches = false;
                switch (Operator)
                {
                    case '^': // Value starts with
                        valueMatches = attributeValue.StartsWith(Value, StringComparison.Ordinal);
                        break;
                    case '$': // Value ends with
                        valueMatches = attributeValue.EndsWith(Value, StringComparison.Ordinal);
                        break;
                    case '=': // Value equals
                        valueMatches = string.Equals(attributeValue, Value, StringComparison.Ordinal);
                        break;
                    default: // No value selector, force true because at least the attribute name matched.
                        valueMatches = true;
                        break;
                }

                return valueMatches;
            }
            else if (Operator == '*')
            {
                return attributeName.Length != Name.Length &
                    attributeName.StartsWith(Name, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return string.Equals(Name, attributeName, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
