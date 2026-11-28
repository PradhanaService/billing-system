// Browser check of meter readings (Playwright), run AFTER full-day.e2e.js on the same app
// (it needs the Sales login, sales person Ravi and one vehicle).
//   node tests/E2E/meter-edit.e2e.js results.json <shots-folder>
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
  const meter = (n, which) => p.locator(`input[aria-label="Meter ${n} ${which}"]`);
  const val = async (n, which) => (await meter(n, which).inputValue()).trim();
  const btn = name => p.getByRole('button', { name, exact: true });
  const see = async text => p.getByText(text, { exact: false }).first().waitFor({ timeout: 8000 });

  await p.goto(BASE + '/account/login'); await p.fill('#username', 'sales'); await p.fill('#password', 'Sales@1234'); await p.click('button[type=submit]'); await settle();
  await p.goto(BASE + '/vehicles'); await settle();
  const code = (await p.locator('tbody tr td').first().innerText()).trim();

  const saveInvoice = async (start, end) => {
    await p.goto(BASE + '/invoices/new'); await settle();
    await p.fill('#vcode', code); await p.press('#vcode', 'Tab'); await settle();
    await p.locator('#createdby').selectOption({ label: 'Ravi' });
    if (start !== null) { await meter(1, 'start').fill(start); await meter(1, 'start').press('Tab'); }
    await meter(1, 'end').fill(end); await meter(1, 'end').press('Tab');
    await btn('Save').click(); await settle(); await see('saved for');
  };

  await step('MTR_E2E_01', 'Three invoices on Meter 1; each new Start is filled from the last End', async () => {
    await saveInvoice('100.0', '110.0');
    await saveInvoice(null, '125.5');
    if (await val(1, 'start') !== '125.5') throw new Error('start was ' + await val(1, 'start'));
    await saveInvoice(null, '130.0');
  });
  await step('MTR_E2E_02', 'Open invoice 2 and correct its End reading to 124.0', async () => {
    await p.goto(BASE + '/invoices/new'); await settle();
    await p.fill('[aria-label="Find invoice"]', '2'); await btn('Search').click(); await settle();
    await btn('Edit meter readings').click(); await settle();
    await meter(1, 'end').fill('124.0'); await meter(1, 'end').press('Tab');
    await p.locator('#mwho').selectOption({ label: 'Ravi' });
    await p.fill('#mreason', 'Misread the dial');
    await btn('Save meter readings').click(); await settle();
    await see('Also updated: invoice 3, Meter 1 start: 125.5 → 124.0');
    await p.locator('section.si-block').filter({ hasText: 'Meter Readings' }).screenshot({ path: `${shots}/MTR_E2E_02.png` });
  });
  await step('MTR_E2E_03', 'Invoice 3 now starts at 124.0 (volume 6.0)', async () => {
    await btn('Next').click(); await settle();
    if (await val(1, 'start') !== '124.0') throw new Error('start ' + await val(1, 'start'));
    if (await val(1, 'volume') !== '6.0') throw new Error('volume ' + await val(1, 'volume'));
  });
  await step('MTR_E2E_04', 'Start readings are locked (new invoice, and Edit meter readings on a saved one)', async () => {
    await p.goto(BASE + '/invoices/new'); await settle();
    if (!(await meter(1, 'start').getAttribute('readonly') !== null)) throw new Error('new invoice start not locked');
    await p.fill('[aria-label="Find invoice"]', '2'); await btn('Search').click(); await settle();
    await btn('Edit meter readings').click(); await settle();
    if (await meter(1, 'start').getAttribute('readonly') === null) throw new Error('edit start not locked');
    if (await meter(1, 'end').getAttribute('readonly') !== null) throw new Error('edit end is locked');
    await btn('Cancel').click(); await settle();
  });
  await step('MTR_E2E_05', 'A correction past the next invoice is refused', async () => {
    await p.goto(BASE + '/invoices/new'); await settle();
    await p.fill('[aria-label="Find invoice"]', '2'); await btn('Search').click(); await settle();
    await btn('Edit meter readings').click(); await settle();
    await meter(1, 'end').fill('140.0'); await meter(1, 'end').press('Tab');
    await p.locator('#mwho').selectOption({ label: 'Ravi' }); await p.fill('#mreason', 'test');
    await btn('Save meter readings').click(); await settle();
    await see('above the end reading of invoice 3');
    await btn('Cancel').click(); await settle();
  });
  await step('MTR_E2E_06', 'Invoice Display > Edit: change only invoice 3 End to 131.0 (same invoice number)', async () => {
    await p.goto(BASE + '/invoices'); await settle();
    await p.locator('tbody tr').filter({ hasText: /^\s*3\b/ }).first().getByRole('button', { name: 'Edit' }).click().catch(async () => {
      await p.locator('tbody tr').first().getByRole('button', { name: 'Edit' }).click(); });
    await p.locator('#ewho').selectOption({ label: 'Ravi' }); await btn('Continue').click(); await settle();
    await p.locator('#emeters input[aria-label="Meter 1 end"]').fill('131.0'); await p.locator('#emeters input[aria-label="Meter 1 end"]').press('Tab'); await settle();
    await p.fill('#emreason', 'Misread the dial');
    await btn('Save').click(); await settle();
    await see('Meter readings of invoice 3 saved');
    await p.goto(BASE + '/invoices/new'); await settle();
    if (await val(1, 'start') !== '131.0') throw new Error('new start ' + await val(1, 'start'));
  });
  await step('MTR_E2E_07', 'Activity Log records the change and the follow-on changes under Ravi', async () => {
    await btn('Sign out').click().catch(() => p.click('text=Sign out')); await settle();
    await p.goto(BASE + '/account/login'); await p.fill('#username', 'admin'); await p.fill('#password', 'Admin@1234'); await p.click('button[type=submit]'); await settle();
    await p.goto(BASE + '/admin/activity-log'); await settle();
    await p.fill('#text', 'METERS'); await btn('Search').click(); await settle();
    const body = await p.locator('tbody').innerText();
    if ((body.match(/INVOICE_METERS_CHANGED/g) || []).length !== 2) throw new Error('changed rows: ' + body);
    if ((body.match(/INVOICE_METERS_ADJUSTED/g) || []).length !== 1) throw new Error('adjusted rows');
    if (!body.includes('Ravi')) throw new Error('no Ravi');
  });
  await step('MTR_E2E_08', 'No browser errors', async () => { if (errors.length) throw new Error(errors.join(' | ')); });
  fs.writeFileSync(out, JSON.stringify(results, null, 2));
  await browser.close();
})();
