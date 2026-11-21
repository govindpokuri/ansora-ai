# Mobile UPI payment approval desk

The administrator records an incoming merchant-bank payment after verifying it manually. This page does not contact a bank or accept a customer's claim as proof, and does not introduce a public UPI QR checkout. Receive payment through your approved merchant UPI setup first.

## Use

Open `https://www.ansora-ai.com/payment-confirm.html` after deployment. Sign in with an email already listed in the private server's `admin_emails`. A fresh email-code verification within 15 minutes is required to approve a payment; ordinary customer accounts cannot approve or view other customers' payment details.

1. Find the customer's existing Ansora account by email.
2. Select the purchased pack, enter the exact received amount, the bank's 12-digit UPI reference/RRN and the payment date/time.
3. Review the recipient, amount and tokens. Check the incoming credit in the merchant bank app, then tick the verification statement and approve.
4. The wallet, captured purchase, token ledger and administrator audit entry are committed together. A repeated request/reference cannot add the same tokens twice. Conflicting use of a reference is rejected.

The current catalogue is Starter ₹599 / 500,000 tokens, Standard ₹999 / 1 million, and Plus ₹2,499 / 3 million. Amounts must match exactly. Overpayments, partial payments, refunds, suspended accounts and non-UPI bank transfers need separate admin review; this form does not adjust those automatically.

The server balance is spendable immediately after approval. An open website dashboard refreshes its balance within 15 seconds (and on focus). Already-open desktop builds may need an account refresh or a new sign-in/listening start to display the new balance. No real-time push to old installed apps is claimed.

## Customer confirmation email

New approvals also queue a branded payment confirmation in the same database transaction. Sending starts only after credits commit, using the existing ZeptoMail, SendGrid, SMTP or PHP-mail configuration. No new key or database table is required. The email contains the amount, token pack, tokens added, account email, receipt reference, approval time and setup steps. It links to `/dashboard` for the latest Windows installer instead of an expiring/versioned installer URL.

The admin page shows email status separately from payment success. **Accepted** means the configured transport accepted the message; it does not prove inbox delivery. Email errors cannot undo a paid purchase or its tokens. Approval retries and duplicate bank references do not automatically send extra receipts. A queued receipt not yet claimed can recover on a replay; once sending was claimed, uncertain/time-out results are not automatically retried because the provider may already have accepted them. Check provider activity and handle support follow-up separately. Older approvals are not emailed retroactively.

Receipt queue, claim and outcome events use deterministic IDs in the existing audit table. Local tests write to the private development payment outbox or use a fake transport; they send no external customer mail. `tests/payment-emails.php` covers post-commit delivery, replay protection, provider failures, incomplete delivery tracking, recovery and escaped receipt content.

If a request times out, check Recent approvals and retry with the **same bank reference**. A new request ID still cannot duplicate a credited UPI reference. An administrator-entered RRN alone is not verification; the bank check is essential.

## iPhone shortcut

In Safari, open the page, tap Share → Add to Home Screen, name it **Ansora Pay**, and enable Open as Web App if offered. This is a website shortcut and needs no Apple Developer membership. Alternatively, create a Shortcut containing the page URL and the Open URLs action. Do not put passwords, OTPs, bearer tokens or an automatic approval request into a shortcut. Authentication and the final bank-verified approval remain on the website.

## Deployment

Use the prepared deployment package. Back up matching server files first. Upload the private files before the public files; no database migration is needed. It reuses the existing `purchases`, `wallets`, `ledger` and `audit` tables and the existing OTP/administrator session checks. No new payment/API key, Composer dependency, scheduled task or webhook is required.

The package includes the current pricing catalogue and loader so the new page and order APIs use the same published amounts. Preserve `config.local.php`, `.env`, storage, vendor files, downloads and all customer data. The public base URL in private configuration must match the website origin, as required by the existing API.

## Verification

`commercial/tests/manual-payments.php` is CLI-only development testing against the explicitly allowed isolated MySQL database `cuemitra_upi_test` on loopback port 3318. It never connects to a production DSN. Start a separate local API instance on port 8199 using only synthetic development configuration, then:

```powershell
$env:ANSORA_UPI_TEST_DSN='mysql:host=127.0.0.1;port=3318;dbname=cuemitra_upi_test;charset=utf8mb4'
php commercial/tests/manual-payments.php http://127.0.0.1:8199
```

Checks cover permissions, fresh admin verification, invalid amounts/references, correct pack credit, replay/conflicting references, concurrent approval races, atomic rollback, audit entries, cross-origin browser requests and immediate customer balance/purchase-history visibility. Test scripts and fixture configuration must not be uploaded to hosting.
