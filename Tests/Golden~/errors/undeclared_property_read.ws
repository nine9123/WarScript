# T3.6: reading a property the instance does not declare raises NameError (plan 5.5).
class P [x]
end
p = new P [1]
y = p :: z   # expect-error: NameError: 'z' is not a field of class 'P'
