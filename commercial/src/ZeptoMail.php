<?php
declare(strict_types=1);
namespace CueMitra;

final class ZeptoMailer {
    public static function ready(array $c): bool {
        return ($c['zeptomail_api_key']??'')!==''
            && !preg_match('/[\r\n]/',(string)$c['zeptomail_api_key'])
            && filter_var($c['zeptomail_from']??'',FILTER_VALIDATE_EMAIL)!==false
            && in_array($c['zeptomail_api_url']??'', ['https://api.zeptomail.in/v1.1/email','https://api.zeptomail.com/v1.1/email'],true);
    }
    public static function send(array $c,string $to,string $subject,string $plain,string $html): void {
        if(!self::ready($c) || !filter_var($to,FILTER_VALIDATE_EMAIL))throw new Problem(503,'ZeptoMail email delivery is not configured.');
        $body=json_encode([
            'from'=>['address'=>$c['zeptomail_from'],'name'=>$c['zeptomail_from_name']??$c['brand']],
            'to'=>[['email_address'=>['address'=>$to]]],
            'subject'=>str_replace(["\r","\n"],'',$subject),'textbody'=>$plain,'htmlbody'=>$html,
            'track_opens'=>false,'track_clicks'=>false,
        ],JSON_THROW_ON_ERROR);
        $curl=curl_init($c['zeptomail_api_url']);
        curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>20,CURLOPT_CONNECTTIMEOUT=>10,
            CURLOPT_FOLLOWLOCATION=>false,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>$body,
            CURLOPT_HTTPHEADER=>['Authorization: Zoho-enczapikey '.$c['zeptomail_api_key'],'Content-Type: application/json','Accept: application/json']]);
        $text=curl_exec($curl); $status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE); $error=curl_errno($curl); curl_close($curl);
        if($text===false || $status<200 || $status>=300) {
            error_log('Ansora ZeptoMail send failed: http='.$status.' curl='.$error);
            throw new Problem(503,'Email delivery failed. Please try again later.');
        }
    }
}
