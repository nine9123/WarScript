# Plan A.24 (CRASH-7, found by the fuzzer, T0.7; fixed by T1.3): an error
# raised inside 'ensure' while another error is propagating. v1 left its
# exception state raised with no exception: the rescue did not run and Run()
# threw a NullReferenceException to the host. The error raised in 'ensure'
# replaces the original one.
begin
    begin
        raise "first"
    ensure
        raise "second"
    end
rescue e
    print "rescued " + e   # expect: rescued second
end
