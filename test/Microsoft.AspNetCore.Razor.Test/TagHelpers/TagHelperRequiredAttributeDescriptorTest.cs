using Xunit;

namespace Microsoft.AspNetCore.Razor.Compilation.TagHelpers
{
    public class TagHelperRequiredAttributeDescriptorTest
    {
        public static TheoryData RequiredAttributeDescriptorData
        {
            get
            {
                // requiredAttributeDescriptor, attributeName, attributeValue, expectedResult
                return new TheoryData<TagHelperRequiredAttributeDescriptor, string, string, bool>
                {
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key"
                        },
                        "KeY",
                        "value",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key"
                        },
                        "keys",
                        "value",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "route-",
                            Operator = '*'
                        },
                        "ROUTE-area",
                        "manage",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "route-",
                            Operator = '*'
                        },
                        "routearea",
                        "manage",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "route-",
                            Operator = '*'
                        },
                        "route-",
                        "manage",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key",
                            IsCssSelector = true,
                        },
                        "KeY",
                        "value",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key",
                            IsCssSelector = true,
                        },
                        "keys",
                        "value",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key",
                            Value = "value",
                            Operator = '=',
                            IsCssSelector = true,
                        },
                        "key",
                        "value",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "key",
                            Value = "value",
                            Operator = '=',
                            IsCssSelector = true,
                        },
                        "key",
                        "Value",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "class",
                            Value = "btn",
                            Operator = '^',
                            IsCssSelector = true,
                        },
                        "class",
                        "btn btn-success",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "class",
                            Value = "btn",
                            Operator = '^',
                            IsCssSelector = true,
                        },
                        "class",
                        "BTN btn-success",
                        false
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "href",
                            Value = "#navigate",
                            Operator = '$',
                            IsCssSelector = true,
                        },
                        "href",
                        "/home/index#navigate",
                        true
                    },
                    {
                        new TagHelperRequiredAttributeDescriptor
                        {
                            Name = "href",
                            Value = "#navigate",
                            Operator = '$',
                            IsCssSelector = true,
                        },
                        "href",
                        "/home/index#NAVigate",
                        false
                    },
                };
            }
        }

        [Theory]
        [MemberData(nameof(RequiredAttributeDescriptorData))]
        public void Matches_ReturnsExpectedResult(
            TagHelperRequiredAttributeDescriptor requiredAttributeDescriptor,
            string attributeName,
            string attributeValue,
            bool expectedResult)
        {
            // Act
            var result = requiredAttributeDescriptor.IsMatch(attributeName, attributeValue);

            // Assert
            Assert.Equal(expectedResult, result);
        }
    }
}
