# How it works: database (write this in your own words)

1. Why is ₹1,250.50 stored as 125050 and not as 1250.5? What could go wrong with `double`?
2. What is a migration? What happens to the database when the app starts?
3. Two people edit the same supplier. Walk through what `Version` does.
4. Why is there a `NormalizedName` column when there is already `Name`?
5. What does `CHECK (OpeningBalancePaise >= 0)` protect against, if the screen already validates?
6. Why must a backup in WAL mode not be a plain file copy?
