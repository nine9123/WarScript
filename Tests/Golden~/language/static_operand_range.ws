# T3.8 (PROP-28): a range loop compares its counter with the upper bound
# before the first iteration, so bounds of known types that '<' rejects are a
# compile error, at the '..'. Bounds from a variable or a call, and the step
# (added only once an iteration completes), raise the runtime TypeError:
# static_operand_range_dynamic.ws.
loop i in 0.."3"   # expect-compile-error: Cannot apply '<' to number and text
    print i
end
