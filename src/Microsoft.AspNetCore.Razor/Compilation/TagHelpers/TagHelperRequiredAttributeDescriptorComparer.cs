﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Razor.Compilation.TagHelpers
{
    /// <summary>
    /// An <see cref="IEqualityComparer{TagHelperRequiredAttributeDescriptor}"/> used to check equality between
    /// two <see cref="TagHelperRequiredAttributeDescriptor"/>s.
    /// </summary>
    public class TagHelperRequiredAttributeDescriptorComparer : IEqualityComparer<TagHelperRequiredAttributeDescriptor>
    {
        /// <summary>
        /// A default instance of the <see cref="TagHelperRequiredAttributeDescriptor"/>.
        /// </summary>
        public static readonly TagHelperRequiredAttributeDescriptorComparer Default =
            new TagHelperRequiredAttributeDescriptorComparer();

        /// <summary>
        /// Initializes a new <see cref="TagHelperRequiredAttributeDescriptor"/> instance.
        /// </summary>
        protected TagHelperRequiredAttributeDescriptorComparer()
        {
        }

        /// <inheritdoc />
        public virtual bool Equals(
            TagHelperRequiredAttributeDescriptor descriptorX,
            TagHelperRequiredAttributeDescriptor descriptorY)
        {
            if (descriptorX == descriptorY)
            {
                return true;
            }

            return descriptorX != null &&
                descriptorX.Operator == descriptorY.Operator &&
                descriptorX.IsCssSelector == descriptorY.IsCssSelector &&
                string.Equals(descriptorX.Name, descriptorY.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(descriptorX.Value, descriptorY.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public virtual int GetHashCode(TagHelperRequiredAttributeDescriptor descriptor)
        {
            var hashCodeCombiner = HashCodeCombiner.Start();
            hashCodeCombiner.Add(descriptor.Operator);
            hashCodeCombiner.Add(descriptor.IsCssSelector);
            hashCodeCombiner.Add(descriptor.Name, StringComparer.OrdinalIgnoreCase);
            hashCodeCombiner.Add(descriptor.Value, StringComparer.Ordinal);

            return hashCodeCombiner.CombinedHash;
        }
    }
}
