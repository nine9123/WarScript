# T3.6: writing a property the instance does not declare raises NameError (PROP-15, plan 5.5).
class P [x]
end
p = new P [1]
p :: z = 4   # expect-error: NameError: 'z' is not a field of class 'P'
