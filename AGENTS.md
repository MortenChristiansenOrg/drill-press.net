# Project

This project is a .NET lint rule engine focused on speed and ease of use.

The primary use case of this tool is for an LLM to find all the places where
it does not follow coding conventions and allow it to fix them in the most
efficient way. All other use cases are secondary to this. It is important that
the output of the tool is highly optimized to LLM consumption. This includes
minimizing the amount of text generated. Other use cases can be supported but
might require opt-in flags for more detailed information, etc.