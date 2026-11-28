// Browser check of advance payments and the Payment History ledger filter (Playwright).
// Run AFTER full-day.e2e.js and meter-edit.e2e.js on the same app: Kaveri Tankers then owes for
// three unpaid trips (3 x 129.68 = 389.04).
//   node tests/E2E/advance-payments.e2e.js results.json <shots-folder>
const { chromium } = require('playwright');
const fs = require('fs');
const [out, shots] = process.argv.slice(2);
const BASE = 'http://localhost:5300';
const results = [];
(async () => {
  const browser = await chromium.launch();
  const p = await (await browser.newContext({ viewport: { width: 1280, height: 1000 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' })).newPage();
  const errors = []; p.on('pageerror', e => errors.push(e.message));
  const settle = async () => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(500); };
  const step = async (id, name, fn) => {
    try { await fn(); results.push({ id, name, ok: true }); console.log('PASS', id, name); }
    catch (e) { results.push({ id, name, ok: false, error: e.message.split('\n')[0] }); console.log('FAIL', id, name, e.message.split('\n')[0]); await p.screenshot({ path: `${shots}/${id}-fail.png`, fullPage: true }); }
  };
  const btn = name => p.getByRole('button', { name, exact: true });
  const see = async text => p.getByText(text, { exact: false }).first().waitFor({ timeout: 8000 });

  await p.goto(BASE + '/account/login'); await p.fill('#username', 'sales'); await p.fill('#password', 'Sales@1234'); await p.click('button[type=submit]'); await settle();
  await p.goto(BASE + '/vehicles'); await settle();
  const code = (await p.locator('tbody tr td').first().innerText()).trim();

  await step('ADV_E2E_01', 'Receipt: tick all pending invoices, type more than their total, see the advance split', async () => {
    await p.goto(BASE + '/receipts'); await settle();
    await p.locator('#rsup').selectOption({ label: 'Kaveri Tankers' }); await settle();
    await p.locator('.ms-box').click();
    await p.locator('.ms-all input').check(); await settle();
    const total = await p.inputValue('#amount');
    if (total !== '389.04') throw new Error('total was ' + total);
    await btn('Done').click();
    await p.fill('#amount', '600'); await settle();
    await see('₹389.04 pays the 3 selected invoices · ₹210.96 kept as advance');
  });
  await step('ADV_E2E_02', 'Save: the message and "Advance with us" show ₹210.96', async () => {
    await btn('Save receipt').click(); await settle();
    await see('₹210.96 is kept as advance and pays the next invoices automatically');
    await see('Advance with us: ₹210.96');
    await p.screenshot({ path: `${shots}/ADV_E2E_02.png`, fullPage: true });
  });
  await step('ADV_E2E_03', 'Next trip is saved Paid from the advance', async () => {
    await p.goto(BASE + '/invoices/new'); await settle();
    await p.fill('#vcode', code); await p.press('#vcode', 'Tab'); await settle();
    await p.locator('#createdby').selectOption({ label: 'Ravi' });
    await btn('Save').click(); await settle(); await see('saved for');
    await btn('Previous').click(); await settle();
    const status = (await p.locator('#paystatus').innerText()).trim();
    if (status !== 'Paid') throw new Error('status ' + status);
  });
  await step('ADV_E2E_04', 'Invoice Display shows the receipt as paid from advance', async () => {
    await p.goto(BASE + '/invoices'); await settle();
    await p.locator('tbody tr').first().getByRole('button', { name: /Paid · Receipt/ }).click(); await settle();
    await see('(from advance)');
  });
  await step('ADV_E2E_05', 'Receipt History and the Receipt screen show the advance left (₹81.28)', async () => {
    await p.goto(BASE + '/receipts'); await settle();
    await p.locator('#rsup').selectOption({ label: 'Kaveri Tankers' }); await settle();
    await see('Advance with us: ₹81.28');
    await p.getByRole('button', { name: /Receipt History/ }).click(); await settle();
    await see('Advance not yet used: ₹81.28');
  });
  await step('ADV_E2E_06', 'Payment History: filter by ledger shows only that ledger and its total', async () => {
    for (const [ledger, amount] of [['Fuel Expense', '50'], ['Fuel Expense', '25'], ['Vehicle Maintenance', '70']]) {
      await p.goto(BASE + '/payments'); await settle();
      await p.locator('#ledger').selectOption({ label: ledger }); await settle();
      await p.locator('#mode').selectOption({ label: 'UPI' });
      await p.fill('#amount', amount); await p.press('#amount', 'Tab');
      await btn('Save payment').click(); await settle(); await see('saved');
    }
    await p.getByRole('button', { name: /Payment History/ }).click(); await settle();
    await p.locator('#fledger').selectOption({ label: 'Fuel Expense' }); await settle();
    const rows = await p.locator('.modal tbody tr').allInnerTexts();
    if (rows.length !== 2 || rows.some(r => !r.includes('Fuel Expense'))) throw new Error('rows: ' + rows.join(' | '));
    const total = await p.locator('#paytotal').innerText();
    if (!total.includes('75.00')) throw new Error('total: ' + total);
  });
  await step('ADV_E2E_07', 'No browser errors', async () => { if (errors.length) throw new Error(errors.join(' | ')); });
  fs.writeFileSync(out, JSON.stringify(results, null, 2));
  await browser.close();
})();
