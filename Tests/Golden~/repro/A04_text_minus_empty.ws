# Plan A.4 (T1.4, T3.6): v1 let an ArgumentException escape Run(); Phase 1 made
# it print "abc". T3.6 removed text subtraction (PROP-7): the operator raises
# TypeError, and Text_remove leaves the text as it is for an empty part.
print Text_remove ["abc", ""]   # expect: abc
x = "abc" - ""                  # expect-error: TypeError: Cannot apply '-' to text and text
