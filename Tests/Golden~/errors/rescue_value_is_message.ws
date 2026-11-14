# A rescued built-in error binds its message text, whatever its kind
# (PROP-22, plan T2.5).
begin
    x = 1 / 0
rescue e
    print e
end
begin
    missing []
rescue e
    print e
end
begin
    raise 42
rescue e
    print e + 1
end
# expect: Division by zero
# expect: Function 'missing' with 0 args is not defined
# expect: 43
