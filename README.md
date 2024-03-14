# trx-time-based-splitting
Generate a optimal split of tests to run in parallel from a TRX file.

Provide a TRX file and it will split the test within it based on their runtime such that each split will take roughly the same time to run. This is intended for running each split in parallel - using the generated split will leads to all parallel test runs end at roughly the same time. 
