// Browser end-to-end run of one full day on a FRESH install, through the real screens (Playwright).
// Start the app on an empty database at http://localhost:5300, then: node tests/E2E/full-day.e2e.js results.json <shots-folder>
// The seed Admin password must be Admin@123 (appsettings Seed:AdminPassword).
const { chromium } = require('playwright');
const fs = require('fs');
const [out, shots] = process.argv.slice(2);
const BASE = 'http://localhost:5300';
const results = [];
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  const settle = async () => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(400); };
  const step = async (id, name, fn) => {
    const t = Date.now();
    try { await fn(); results.push({ id, name, ok: true, ms: Date.now() - t }); console.log('PASS', id, name); }
    catch (e) { results.push({ id, name, ok: false, ms: Date.now() - t, error: String(e.message).split('\n')[0] });
                console.log('FAIL', id, name, e.message.split('\n')[0]); await p.screenshot({ path: `${shots}/${id}-fail.png` }); }
  };
  const login = async (u, pw) => { await p.goto(BASE + '/account/login'); await p.fill('#username', u); await p.fill('#password', pw); await p.click('button[type=submit]'); await settle(); };
  const signOut = async () => { await p.click('text=Sign out'); await settle(); };
  const btn = name => p.getByRole('button', { name, exact: true });
  const see = async text => { await p.getByText(text, { exact: false }).first().waitFor({ timeout: 8000 }); };

  await step('E2E_01', 'Admin first sign-in forces a new password', async () => {
    await login('admin', 'Admin@123');
    if (!p.url().includes('change-password')) throw new Error('not sent to change password');
    await p.fill('#current', 'Admin@123'); await p.fill('#new', 'Admin@1234'); await p.fill('#confirm', 'Admin@1234');
    await p.click('button[type=submit]'); await settle();
    await see('Dashboard');
  });
  await step('E2E_02', 'Password Management creates the Sales login', async () => {
    await p.goto(BASE + '/admin/users'); await settle();
    await p.fill('#new-SalesPerson', 'Sales@1234'); await p.fill('#confirm-SalesPerson', 'Sales@1234');
    await btn('Create Sales Person login').click();
    await see('Sales Person password changed');
  });
  await step('E2E_03', 'Sales Persons: add Ravi', async () => {
    await p.goto(BASE + '/admin/sales-persons'); await settle();
    await p.fill('#name', 'Ravi'); await btn('Add').click(); await settle();
    await see('Ravi');
  });
  await step('E2E_04', 'Supplier Master: add Kaveri Tankers', async () => {
    await p.goto(BASE + '/suppliers'); await settle();
    await p.getByRole('button', { name: 'Add Supplier' }).click();
    await p.fill('#name', 'Kaveri Tankers'); await p.fill('#short', 'Kaveri'); await p.fill('#phone', '9123456780');
    await btn('Save').click(); await settle();
    await see('Kaveri Tankers');
  });
  await step('E2E_05', 'Vehicle Master: add A2 TN 67 C at 129.68 per trip', async () => {
    await p.goto(BASE + '/vehicles'); await settle();
    await p.getByRole('button', { name: 'Add vehicle' }).click(); await settle();
    await p.fill('#vno', 'TN 67 C'); await p.locator('#supplier').selectOption({ label: 'Kaveri Tankers' });
    await p.fill('#cap', '24000'); await p.fill('#rate', '129.68');
    const code = await p.inputValue('#code'); if (!code) await p.fill('#code', 'A2');
    await btn('Save').click(); await settle();
    await see('TN 67 C');
  });
  let code = '';
  await step('E2E_06', 'Supplier name opens its vehicles page', async () => {
    await p.goto(BASE + '/suppliers'); await settle();
    await p.getByRole('link', { name: 'Kaveri Tankers' }).click(); await settle();
    if (!/\/suppliers\/\d+\/vehicles/.test(p.url())) throw new Error('wrong page ' + p.url());
    await see('TN 67 C');
    code = (await p.locator('tbody tr td').first().innerText()).trim();
  });
  await step('E2E_07', 'Admin takes a backup', async () => {
    await p.goto(BASE + '/admin/backup'); await settle();
    await p.getByRole('button', { name: 'Backup now' }).click();
    await see('Backup taken');
  });
  await signOut();

  await step('E2E_08', 'Sales login signs in; Admin pages are not found', async () => {
    await login('sales', 'Sales@1234');
    await see('Dashboard');
    await p.goto(BASE + '/admin/backup'); await settle();
    await see('Page not found');
  });
  await step('E2E_09', 'Sales Invoice: typed vehicle code fills the details; credit invoice saves', async () => {
    await p.goto(BASE + '/invoices/new'); await settle();
    await p.fill('#vcode', code); await p.press('#vcode', 'Tab'); await settle();
    await see('Found in Vehicle Master');
    await p.locator('#createdby').selectOption({ label: 'Ravi' });
    await p.fill('#intime', '11:30 AM'); await p.press('#intime', 'Tab');
    await p.fill('#outtime', '11:55 AM'); await p.press('#outtime', 'Tab');
    await btn('Save').click(); await settle();
    await see('Invoice 1 saved for ₹129.68');
  });
  await step('E2E_10', 'Invoice Display lists it as Not paid', async () => {
    await p.goto(BASE + '/invoices'); await settle();
    await p.locator('tbody').getByText('TN 67 C').first().waitFor({ timeout: 8000 });
    await p.locator('tbody').getByText('Not paid').first().waitFor({ timeout: 8000 });
  });
  await step('E2E_11', 'Receipt: tick the pending invoice, amount fills, save', async () => {
    await p.goto(BASE + '/receipts'); await settle();
    await p.locator('#rsup').selectOption({ label: 'Kaveri Tankers' }); await settle();
    await p.locator('.ms-box').click();
    await p.getByLabel('Invoice 1', { exact: true }).check(); await settle();
    const amount = await p.inputValue('#amount');
    if (amount !== '129.68') throw new Error('amount was ' + amount);
    await btn('Done').click();
    await p.locator('#mode').selectOption({ label: 'UPI' });
    await btn('Save receipt').click(); await settle();
    await see('Receipt 1 saved for ₹129.68');
  });
  await step('E2E_12', 'Invoice Display now shows Paid · Receipt 1', async () => {
    await p.goto(BASE + '/invoices'); await settle();
    await see('Paid · Receipt 1');
  });
  await step('E2E_13', 'Payment: fuel by Bank without a reference', async () => {
    await p.goto(BASE + '/payments'); await settle();
    await p.locator('#ledger').selectOption({ label: 'Fuel Expense' }); await settle();
    await p.locator('#mode').selectOption({ label: 'Bank' });
    await p.fill('#amount', '50'); await p.press('#amount', 'Tab');
    await btn('Save payment').click(); await settle();
    await see('Payment 1 saved.');
  });
  await step('E2E_14', 'Vehicle Trip Report: totals and PDF', async () => {
    await p.goto(BASE + '/reports'); await settle();
    await see('All suppliers · all vehicles');
    const totals = await p.locator('#totals').innerText();
    if (!totals.includes('₹129.68') || !totals.includes('₹0.00')) throw new Error('totals: ' + totals.replace(/\n/g, ' '));
    await p.locator('#supplier').selectOption({ label: 'Kaveri Tankers' }); await settle();
    await see('Vehicle subtotal');
    const href = await p.getAttribute('#export', 'href');
    const pdf = await p.request.get(BASE + href);
    if (pdf.status() !== 200 || (pdf.headers()['content-type'] || '') !== 'application/pdf') throw new Error('pdf ' + pdf.status());
    await p.screenshot({ path: `${shots}/E2E_14-report.png`, fullPage: true });
  });
  await step('E2E_15', 'Invoice Edit: Ravi corrects the rate; a corrected copy is issued', async () => {
    await p.goto(BASE + '/invoices'); await settle();
    await btn('Edit').first().click();
    await p.locator('#ewho').selectOption({ label: 'Ravi' });
    await btn('Continue').click(); await settle();
    await p.fill('#erate', '130'); await p.press('#erate', 'Tab');
    await p.getByRole('button', { name: /Save/ }).first().click(); await settle();
    await see('corrected copy is invoice 2');
  });
  await signOut();
  await step('E2E_16', 'Activity Log shows Ravi as Done by on the Sales login', async () => {
    await login('admin', 'Admin@1234');
    await p.goto(BASE + '/admin/activity-log'); await settle();
    await p.locator('#user').selectOption({ label: 'Ravi' });
    await btn('Search').click(); await settle();
    await see('INVOICE_EDITED'); await see('Sales login');
  });
  await step('E2E_17', 'Restore the morning backup: data goes back, everyone signed out', async () => {
    await p.goto(BASE + '/admin/backup'); await settle();
    const rows = p.locator('tbody tr');
    await rows.last().getByRole('button', { name: 'Restore' }).click();
    await p.fill('#rbconf', 'RESTORE');
    await p.locator('.modal').getByRole('button', { name: 'Restore', exact: true }).click();
    await p.waitForURL(/account\/login/, { timeout: 15000 }); await settle();
    await see('The database was restored from a backup');
    await login('admin', 'Admin@1234');
    await p.goto(BASE + '/invoices'); await settle();
    if (await p.getByText('TN 67 C').count() > 0) throw new Error('invoice still there after restore');
  });
  await step('E2E_18', 'No browser errors during the run', async () => { if (errors.length) throw new Error(errors.join(' | ')); });

  fs.writeFileSync(out, JSON.stringify(results, null, 2));
  await browser.close();
})();
