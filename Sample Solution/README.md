# Sample target solution

This intentionally imperfect .NET 10/C# 14 solution exercises the AOT proof of
concept in `../AOT POC`.

It contains violations for all seven sample rules. `IWidgetStore` has one
production implementation and one test-only implementation, demonstrating that
the solution-wide DP1003 rule excludes test projects. `IWidgetFormatter` has two
production implementations and is the non-violation control case.

`Invalid_input_throws` is another non-violation control: its sole assertion is
`Assert.Throws`, placed before the method's second empty line. It exercises the
explicit exception on the assertion-ordering rule.

All projects enable nullable reference types through `Directory.Build.props`.
