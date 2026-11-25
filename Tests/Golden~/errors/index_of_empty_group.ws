# An empty group has no value to index (found by the fuzzer: it was a host
# fault, "Stack empty", in the parser).
print ()[0]   # expect-compile-error: Malformed expression: '[' has nothing to index
