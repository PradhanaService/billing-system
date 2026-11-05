# Requirement-to-test table

Generated from a test run: **360 of 360 tests passed**. Regenerate with `tools/requirement_matrix.py` (see the file for the command).

| Requirement | Tests | Result |
|---|---|---|
| FR-ADM-01 | FR_ADM_01_Admin_Creates_User_With_Must_Change_Password | Pass (1) |
| FR-ADM-02 | FR_ADM_02_Temporary_Password_Forces_Change_Before_Anything_Else | Pass (1) |
| FR-ADM-03 | FR_ADM_03_Failed_Login_Is_Refused_And_Logged | Pass (1) |
| FR-ADM-04 | FR_ADM_04_Log_Shows_Who_Did_What_With_Before_And_After | Pass (1) |
| FR-ADM-05 | FR_ADM_05_Activity_Log_Rows_Cannot_Be_Changed_Or_Deleted | Pass (1) |
| FR-ADM-07 | FR_ADM_07_Only_Admin_Sees_Activity_Log<br>FR_ADM_07_Only_Admin_Views_And_Viewing_Is_Logged | Pass (2) |
| FR-DASH-01 | FR_DASH_01_02_Cards_Match_The_Transactions | Pass (1) |
| FR-DASH-04 | FR_DASH_04_Six_Months_Ending_With_The_Selected_Month | Pass (1) |
| FR-DASH-08 | FR_DASH_08_Month_Selector_Changes_Month_Figures_Not_Today | Pass (1) |
| FR-DAY-03 | FR_DAY_03_Earlier_Open_Day_Blocks_Closing | Pass (1) |
| FR-DAY-04 | FR_DAY_04_Exact_Count_Closes_Without_Reason_Or_Posting<br>FR_DAY_04_P8_Cash_Short_Needs_Reason_And_Posts<br>FR_DAY_04_P9_Cash_Over_Posts_The_Other_Way | Pass (3) |
| FR-DAY-05 | FR_DAY_05_Closing_Locks_The_Day_And_Carries_Cash_Forward | Pass (1) |
| FR-DSP-08 | FR_DSP_08_Business_Day_Runs_6pm_To_6pm | Pass (1) |
| FR-INV-01 | FR_INV_01_Admin_Sets_Starting_Invoice_Number_Only_Before_First_Use<br>FR_INV_01_Numbers_Are_Gapless_And_Shown_Before_Save<br>FR_INV_01_Numbers_Are_Gapless_Even_After_A_Failed_Save | Pass (3) |
| FR-INV-03 | FR_INV_03_Amount_Rounding<br>FR_INV_03_RoundHalfUp_To_Paise<br>FR_INV_03_Trip_Amount_Is_Trips_Times_Rate<br>FR_INV_03_Volume_Vehicle_Needs_Volume_And_Bills_By_It | Pass (4) |
| FR-INV-04 | FR_INV_04_Rate_Change_By_Admin_Needs_Reason_And_Is_Logged<br>FR_INV_04_Rate_Change_By_Any_Role_Needs_A_Reason | Pass (2) |
| FR-INV-07 | FR_INV_07_Nothing_Is_Saved_When_Posting_Fails | Pass (1) |
| FR-INV-08 | FR_INV_08_Bill_To_Customer_Posts_To_Customer_Tagged_With_Vehicle | Pass (1) |
| FR-INV-12 | FR_INV_12_Cancel_Reverses_Keeps_Number_And_Needs_Reason | Pass (1) |
| FR-INV-14 | FR_INV_14_Previous_And_Next | Pass (1) |
| FR-INV-15 | FR_INV_15_Period_Display_Totals_Match_The_Days | Pass (1) |
| FR-INV-16 | FR_INV_16_Closing_Is_Opening_Plus_Cash_Sales_Plus_Receipts_Minus_Payments | Pass (1) |
| FR-LED-01 | FR_LED_01_Each_Line_Has_Exactly_One_Positive_Side<br>FR_LED_01_P1_Credit_Sale_Posts_Party_And_Sales<br>FR_LED_01_Unbalanced_Posting_Is_Refused | Pass (3) |
| FR-LED-02 | FR_LED_02_Ledger_Lines_Cannot_Be_Updated_Or_Deleted | Pass (1) |
| FR-LED-04 | FR_LED_04_Running_Balance_Stored_Per_Account_And_Party<br>FR_LED_04_Statement_Balance_Matches_Stored_Running_Balance | Pass (2) |
| FR-LED-05 | FR_LED_05_Supplier_Statement_Has_Opening_Totals_And_Closing | Pass (1) |
| FR-LED-06 | FR_LED_06_Sort_By_Value_Date_Moves_The_Reversal_To_Its_Original_Day | Pass (1) |
| FR-LED-07 | FR_LED_07_Vehicle_Balance_Is_A_View_Of_Tagged_Lines<br>FR_LED_07_Vehicle_Statement_Shows_Only_Its_Tagged_Lines | Pass (2) |
| FR-LED-09 | FR_LED_09_Reversal_Swaps_Sides_And_Cites_Original | Pass (1) |
| FR-LED-10 | FR_LED_10_Closed_Day_Refuses_Postings_And_Corrections_Keep_Value_Date | Pass (1) |
| FR-RPT-02 | FR_RPT_02_Vehicle_Report_Has_Supplier_Subtotals_And_Grand_Total | Pass (1) |
| FR-RPT-06 | FR_RPT_06_Supplier_And_Vehicle_Filters | Pass (1) |
| FR-RPT-07 | FR_RPT_07_Outstanding_Equals_Party_Ledger_Balances<br>FR_RPT_07_Sales_Report_Totals_Equal_Water_Sales_Ledger<br>FR_RPT_07_Trial_Balance_Balances_And_Matches_Ledgers | Pass (3) |
| FR-TXN-03 | FR_TXN_03_Reference_Required_For_Bank_And_Upi | Pass (1) |
| FR-TXN-05 | FR_TXN_05_Amount_Must_Be_Above_Zero | Pass (1) |
| FR-TXN-06 | FR_TXN_06_P3_Receipt_Posts_Cash_And_Party | Pass (1) |
| FR-TXN-08 | FR_TXN_08_Cash_Above_Drawer_Needs_Confirmation | Pass (1) |
| FR-TXN-09 | FR_TXN_09_Allocates_Oldest_First_And_Keeps_Remainder_Unallocated | Pass (1) |
| FR-TXN-10 | FR_TXN_10_Part_Payment_Moves_Vehicle_Balance | Pass (1) |
| FR-TXN-11 | FR_TXN_11_Cancel_Payment_Restores_Cash<br>FR_TXN_11_Cancel_Reverses_And_Frees_The_Invoices | Pass (2) |
| FR-TXN-12 | FR_TXN_12_History_Filters_By_Mode_And_Status | Pass (1) |
| FR-TXN-13 | FR_TXN_13_Creates_Expense_Ledger_With_Unique_Name<br>FR_TXN_13_Ledger_With_Entries_Cannot_Be_Deleted<br>FR_TXN_13_System_Ledgers_Cannot_Be_Changed_Or_Deleted | Pass (3) |
| FR-VM-01 | FR_VM_01_Creates_Supplier_Tidied_And_Logged<br>FR_VM_01_Duplicate_Name_Ignoring_Case_Is_Refused<br>FR_VM_01_Phone_Is_Required_And_Checked<br>FR_VM_01_Supplier_Name_Is_Unique_Ignoring_Case_And_Spaces | Pass (4) |
| FR-VM-03 | FR_VM_03_A_Typed_Code_Must_Be_Valid_And_Unused<br>FR_VM_03_Code_Of_Deleted_Vehicle_Is_Never_Reused<br>FR_VM_03_Codes_Are_Proposed_In_Order | Pass (3) |
| FR-VM-04 | FR_VM_04_Vehicle_No_Format<br>FR_VM_04_Vehicle_No_Is_Uppercase_Tidied_And_Unique_Ignoring_Spaces | Pass (2) |
| FR-VM-05 | FR_VM_05_Nickname_Defaults_To_Short_Name_And_Plate | Pass (1) |
| FR-VM-08 | FR_VM_08_Party_With_Opening_Entry_Cannot_Be_Deleted<br>FR_VM_08_Supplier_With_Vehicles_Cannot_Be_Deleted<br>FR_VM_08_Supplier_Without_History_Can_Be_Deleted | Pass (3) |
| FR-VM-09 | FR_VM_09_Inactive_Vehicle_Or_Supplier_Cannot_Be_Invoiced<br>FR_VM_09_Only_Active_Vehicles_Of_Active_Suppliers_Are_Billable | Pass (2) |
| FR-VM-10 | FR_VM_10_Search_And_Status_Filter<br>FR_VM_10_Search_By_Number_Code_Name_Or_Supplier | Pass (2) |
| FR-VM-11 | FR_VM_11_Creates_Customer_And_Refuses_Duplicate | Pass (1) |
| FR-VM-12 | FR_VM_12_Opening_Balance_Cannot_Change_After_Creation<br>FR_VM_12_P10_Cr_Opening_Balance_Is_Reversed_Sides<br>FR_VM_12_P10_Dr_Opening_Balance_Posts_Once | Pass (3) |
| NFR-AUD-03 | NFR_AUD_03_Utc_Shown_As_Ist | Pass (1) |
| NFR-CON-03 | NFR_CON_03_Second_Writer_Waits_For_The_First | Pass (1) |
| NFR-CON-05 | NFR_CON_05_Second_Editor_Of_Same_Supplier_Is_Refused<br>NFR_CON_05_Stale_Edit_Is_Refused | Pass (2) |
| NFR-SEC-05 | NFR_SEC_05_Not_Logged_In_Is_Refused<br>NFR_SEC_05_Only_Admin_Can_Manage_Users<br>NFR_SEC_05_Pages_Need_Login<br>NFR_SEC_05_Server_Allows_Only_What_The_Role_Table_Allows | Pass (4) |
| NFR-USE-02 | NFR_USE_02_Date_Format<br>NFR_USE_02_Format_Uses_Indian_Grouping | Pass (2) |

56 requirements are covered by named tests. Other tests (startup, layout, helpers) support these but are not tied to one requirement ID.
