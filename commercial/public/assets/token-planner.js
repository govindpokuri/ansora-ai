'use strict';
// Extrapolate a user-reported sample. No provider calls or billing changes.
const planner=document.getElementById('token-planner');
const plannerFormat=new Intl.NumberFormat('en-IN',{maximumFractionDigits:1});
function updateTokenPlan(){
    const budget=Number(document.getElementById('planner-budget').value);
    const tokens=Number(document.getElementById('planner-sample-tokens').value);
    const sampleMinutes=Number(document.getElementById('planner-sample-minutes').value);
    const length=Number(document.getElementById('planner-length').value);
    const fields=['planner-sample-tokens','planner-sample-minutes','planner-length'].map(id=>document.getElementById(id));
    const valid=fields.every(field=>field.checkValidity())&&Number.isFinite(budget)&&budget>0;
    document.getElementById('planner-result').hidden=!valid;
    document.getElementById('planner-invalid').hidden=valid;
    if(!valid)return;
    const perMinute=tokens/sampleMinutes;
    const minutes=budget/perMinute;
    document.getElementById('planner-minutes').textContent='≈ '+plannerFormat.format(minutes)+' minutes';
    document.getElementById('planner-interviews').textContent='About '+Math.floor(minutes/length)+' complete interviews of '+length+' minutes each';
    document.getElementById('planner-assumptions').textContent=plannerFormat.format(tokens)+' tokens over '+plannerFormat.format(sampleMinutes)+' minutes = '+plannerFormat.format(perMinute)+' tokens/minute. '+plannerFormat.format(budget)+' ÷ '+plannerFormat.format(perMinute)+' ≈ '+plannerFormat.format(minutes)+' minutes.';
}
planner.addEventListener('input',updateTokenPlan);
planner.addEventListener('submit',event=>{event.preventDefault();updateTokenPlan();});
updateTokenPlan();
