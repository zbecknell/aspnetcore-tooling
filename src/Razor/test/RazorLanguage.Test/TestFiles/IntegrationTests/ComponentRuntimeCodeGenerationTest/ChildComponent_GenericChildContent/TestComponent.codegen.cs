// <auto-generated/>
#pragma warning disable 1591
namespace Test
{
    #line hidden
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    public class TestComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        #pragma warning disable 1998
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.RenderTree.RenderTreeBuilder builder)
        {
            builder.OpenComponent<Test.MyComponent<string>>(0);
            builder.AddAttribute(1, "Item", Microsoft.AspNetCore.Components.RuntimeHelpers.TypeCheck<string>(
#nullable restore
#line 1 "x:\dir\subdir\Test\TestComponent.cshtml"
                                  "hi"

#line default
#line hidden
#nullable disable
            ));
            builder.AddAttribute(2, "ChildContent", (Microsoft.AspNetCore.Components.RenderFragment<string>)((context) => (builder2) => {
                builder2.AddMarkupContent(3, "\r\n  ");
                builder2.OpenElement(4, "div");
                builder2.AddContent(5, 
#nullable restore
#line 2 "x:\dir\subdir\Test\TestComponent.cshtml"
        context.ToLower()

#line default
#line hidden
#nullable disable
                );
                builder2.CloseElement();
                builder2.AddMarkupContent(6, "\r\n");
            }
            ));
            builder.CloseComponent();
        }
        #pragma warning restore 1998
    }
}
#pragma warning restore 1591