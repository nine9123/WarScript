# A native given an argument of the wrong type is a TypeError whose message
# names the native (plan 6.9, T2.5).
r = sqrt ["four"]   # expect-error: TypeError: Native function 'sqrt' failed: Argument 0 expected Numeric
