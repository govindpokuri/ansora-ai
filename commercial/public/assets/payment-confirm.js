'use strict';
const el=id=>document.getElementById(id);
const money=n=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',maximumFractionDigits:2}).format(n/100);
const count=n=>Number(n).toLocaleString('en-IN');
let plans=[],customer=null,challenge='',attempt=null,approved=false;
function feedback(text){el('feedback').textContent=text;el('feedback').hidden=!text;}
function login(){el('workspace').hidden=true;el('login-card').hidden=false;el('email-form').hidden=false;el('code-form').hidden=true;challenge='';el('code').value='';el('bank-verified').checked=false;el('approve').disabled=true;}
async function api(route,body){
    const response=await fetch('/api/'+route,{method:body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',headers:body===undefined?{}:{'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
    let data;try{data=await response.json();}catch{throw new Error('The server response could not be read. Check recent approvals before retrying.');}
    if(!response.ok){if(response.status===401)login();const error=new Error(data.error||'The request could not be completed.');error.status=response.status;throw error;}return data;
}
async function busy(button,action){button.disabled=true;try{await action();}catch(error){feedback(error.message);}finally{button.disabled=false;}}
function setDate(){const now=new Date();const local=new Date(now.getTime()-now.getTimezoneOffset()*60000);el('received-at').value=local.toISOString().slice(0,16);}
function selectedPlan(){return plans.find(plan=>plan.id===el('plan').value);}
function showPack(){const plan=selectedPlan();el('pack-summary').textContent=plan?money(plan.price_paise)+' · '+count(plan.tokens)+' AI tokens':'';}
async function recent(){
    const data=await api('admin/upi/recent');plans=data.plans;
    const previous=el('plan').value;el('plan').replaceChildren();
    for(const plan of plans){const option=document.createElement('option');option.value=plan.id;option.textContent=plan.name+' · '+money(plan.price_paise)+' · '+count(plan.tokens)+' tokens';el('plan').append(option);}
    el('plan').value=plans.some(p=>p.id===previous)?previous:'standard';showPack();
    const list=el('recent-list');list.replaceChildren();
    if(!data.payments.length){const p=document.createElement('p');p.className='muted';p.textContent='No manual UPI approvals yet.';list.append(p);}
    for(const payment of data.payments){const row=document.createElement('article');row.className='payment-item';const title=document.createElement('strong');title.textContent=payment.email;const detail=document.createElement('span');detail.textContent=money(payment.amount)+' · '+count(payment.tokens)+' tokens';const meta=document.createElement('span');meta.textContent='RRN '+payment.payment_id.replace(/^upi_/,'')+' · '+new Date(payment.created_at*1000).toLocaleString('en-IN');row.append(title,detail,meta);list.append(row);}
}
async function boot(){
    try{const account=await api('account');el('logout').hidden=false;if(account.user.role!=='admin'){login();feedback('This page requires an administrator account. Sign out and use your owner email.');return;}
        await recent();el('login-card').hidden=true;el('workspace').hidden=false;
    }catch(error){login();feedback(error.status===401?'':error.message);}
}
el('email-form').addEventListener('submit',async event=>{event.preventDefault();await busy(event.submitter,async()=>{const result=await api('auth/start',{email:el('admin-email').value.trim(),terms_accepted:el('terms').checked});challenge=result.challenge;el('email-form').hidden=true;el('code-form').hidden=false;el('code').focus();feedback('Sign-in code sent. Check your inbox and spam folder.');});});
el('code-form').addEventListener('submit',async event=>{event.preventDefault();await busy(event.submitter,async()=>{await api('auth/verify',{challenge,code:el('code').value.trim()});el('code').value='';feedback('');await boot();});});
el('change-email').addEventListener('click',()=>{el('email-form').hidden=false;el('code-form').hidden=true;challenge='';});
el('logout').addEventListener('click',async()=>{try{await api('auth/logout',{});location.reload();}catch(error){feedback(error.message);}});
function invalidateCustomer(){customer=null;attempt=null;el('customer-card').hidden=true;el('entry-card').hidden=true;el('review-card').hidden=true;el('success-card').hidden=true;}
el('customer-email').addEventListener('input',invalidateCustomer);
el('find-form').addEventListener('submit',async event=>{event.preventDefault();await busy(event.submitter,async()=>{invalidateCustomer();feedback('');const data=await api('admin/upi/account?email='+encodeURIComponent(el('customer-email').value.trim()));if(data.user.status!=='active')throw new Error('This account is suspended. Review it in the full admin portal.');customer=data;el('customer-name').textContent=data.user.email;el('customer-balance').textContent=count(data.wallet.available)+' AI tokens available';el('customer-card').hidden=false;el('entry-card').hidden=false;approved=false;el('reference').value='';el('amount').value='';setDate();});});
el('plan').addEventListener('change',showPack);
function amountPaise(text){if(!/^\d+(\.\d{1,2})?$/.test(text))throw new Error('Enter the received rupee amount with up to two decimal places.');const parts=text.split('.');const value=Number(parts[0])*100+Number((parts[1]||'').padEnd(2,'0'));if(!Number.isSafeInteger(value))throw new Error('The received amount is invalid.');return value;}
el('payment-form').addEventListener('submit',event=>{event.preventDefault();try{
    if(!customer)throw new Error('Find the customer account first.');const plan=selectedPlan();if(!plan)throw new Error('Select a valid token pack.');
    const amount=amountPaise(el('amount').value.trim());if(amount!==plan.price_paise)throw new Error('The bank amount must match '+money(plan.price_paise)+' for this pack.');
    const reference=el('reference').value.trim();if(!/^\d{12}$/.test(reference))throw new Error('Enter the 12-digit UPI bank reference.');
    const received=Math.floor(new Date(el('received-at').value).getTime()/1000);if(!Number.isFinite(received))throw new Error('Enter the payment date and time.');
    const payload={user_id:customer.user.id,email:customer.user.email,plan_id:plan.id,amount_received_paise:amount,bank_reference:reference,received_at:received,bank_received:true};
    const fingerprint=JSON.stringify(payload);if(!attempt||attempt.fingerprint!==fingerprint)attempt={...payload,request_id:crypto.randomUUID(),fingerprint};
    const details=el('review-details');details.replaceChildren();for(const [label,value] of [['Customer',customer.user.email],['Amount',money(amount)],['Pack',plan.name],['AI tokens',count(plan.tokens)],['UPI reference',reference],['Received',new Date(received*1000).toLocaleString('en-IN')]]){const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;dd.textContent=value;details.append(dt,dd);}
    el('entry-card').hidden=true;el('review-card').hidden=false;el('bank-verified').checked=false;el('approve').disabled=true;el('approve').textContent='Approve & add '+count(plan.tokens)+' tokens';feedback('');el('review-card').scrollIntoView({block:'start',behavior:'auto'});
}catch(error){feedback(error.message);}});
el('bank-verified').addEventListener('change',()=>{el('approve').disabled=!el('bank-verified').checked;});
el('edit').addEventListener('click',()=>{el('review-card').hidden=true;el('entry-card').hidden=false;el('bank-verified').checked=false;});
el('approve').addEventListener('click',async()=>{
    if(approved||!attempt||!el('bank-verified').checked)return;
    const button=el('approve');el('edit').disabled=true;
    await busy(button,async()=>{const {fingerprint,...payload}=attempt;let result;try{result=await api('admin/upi/confirm',payload);}catch(error){throw new Error(error.message+' Check Recent approvals before retrying with the same bank reference.');}if(result.ok!==true||typeof result.credited!=='boolean'||!Number.isSafeInteger(result.wallet?.available))throw new Error('Approval response could not be confirmed. Check Recent approvals before retrying the same reference.');approved=true;el('review-card').hidden=true;el('entry-card').hidden=true;el('success-card').hidden=false;el('success-title').textContent=result.credited?'Tokens added':'Payment already approved';el('success-email').textContent=attempt.email;el('success-balance').textContent=count(result.wallet.available)+' AI tokens available';el('success-reference').textContent='UPI reference '+attempt.bank_reference;el('customer-balance').textContent=count(result.wallet.available)+' AI tokens available';feedback('');try{await recent();}catch{feedback('Approval succeeded. Recent history could not refresh; do not pay or approve again.');}});
    el('edit').disabled=false;button.disabled=approved||!el('bank-verified').checked;
});
el('new-payment').addEventListener('click',()=>{approved=false;attempt=null;el('success-card').hidden=true;el('entry-card').hidden=!customer;el('reference').value='';el('amount').value='';el('bank-verified').checked=false;setDate();feedback('');});
el('refresh').addEventListener('click',async event=>busy(event.currentTarget,recent));
setDate();boot();
