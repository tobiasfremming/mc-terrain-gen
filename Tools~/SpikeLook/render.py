# render.py <obj> <out.png> [palette] [views]: z-buffered, coloured contact sheet of a dumped creature OBJ.
import sys, json, numpy as np
from PIL import Image, ImageDraw

def load(path):
    V=[];P=[];N=[];D=[];B=[];F=[]
    for line in open(path):
        if line[0]=='v':
            a=line.split(); V.append([float(a[1]),float(a[2]),float(a[3])]); P.append(int(round(float(a[4]))))
            N.append([float(a[5]),float(a[6]),float(a[7])]); D.append(float(a[8])); B.append(int(a[9]))
        elif line[0]=='f':
            a=line.split(); F.append([int(a[1])-1,int(a[2])-1,int(a[3])-1])
    return dict(V=np.array(V),P=np.array(P),N=np.array(N),D=np.array(D),B=np.array(B),F=np.array(F))

def srgb(c): return np.array(c,float)
PAL = {
 'king': dict(dorsal=(0.55,0.10,0.06), flank=(0.80,0.25,0.10), belly=(0.93,0.85,0.70), speck=(0.98,0.80,0.55), speckAmt=0.25,
              parts={1:(0.72,0.16,0.08),2:(0.20,0.08,0.06),9:(0.92,0.80,0.62),3:(0.03,0.03,0.03),11:(0.30,0.05,0.05),12:(0.75,0.18,0.08),13:(0.15,0.07,0.06)}),
 'stone': dict(dorsal=(0.40,0.16,0.12), flank=(0.48,0.24,0.17), belly=(0.75,0.62,0.48), speck=(0.62,0.60,0.55), speckAmt=0.45,
              parts={1:(0.46,0.22,0.15),2:(0.08,0.06,0.05),9:(0.62,0.52,0.40),3:(0.03,0.03,0.03),11:(0.4,0.2,0.14),12:(0.42,0.18,0.13),13:(0.06,0.05,0.05)}),
}
def rot(yaw, pitch):
    cy,sy,cp,sp=np.cos(yaw),np.sin(yaw),np.cos(pitch),np.sin(pitch)
    Ry=np.array([[cy,0,-sy],[0,1,0],[sy,0,cy]]); Rx=np.array([[1,0,0],[0,cp,-sp],[0,sp,cp]])
    return Rx@Ry
VIEWS={'top':rot(np.pi,np.pi/2),'side':rot(-np.pi/2,0.0),'back':rot(np.pi,0.05),'rear34':rot(np.pi-np.pi*0.25,0.45),
       'low34':rot(np.pi-np.pi*0.3,0.15),'front34':rot(np.pi*0.2,0.5),'below':rot(np.pi-0.3,-0.6),'front':rot(0.0,0.12),'lowfront34':rot(np.pi*0.25,0.2)}

def hashnoise(X, scale):
    q=np.floor(X*scale).astype(np.int64)
    h=(q[:,0]*73856093)^(q[:,1]*19349663)^(q[:,2]*83492791)
    h=(h^(h>>13))*1274126177; h=h^(h>>16)
    return ((h&0xFFFF)/65535.0)

def colours(m, pal):
    P,D,V=m['P'],m['D'],m['V']
    dor,fl,be=srgb(pal['dorsal']),srgb(pal['flank']),srgb(pal['belly'])
    t=np.clip(D,-1,1)[:,None]
    body=fl+(dor-fl)*np.maximum(t,0)+(be-fl)*np.maximum(-t,0)
    n=hashnoise(V,180.0)
    spk=(n>0.82)[:,None]*pal['speckAmt']
    body=body*(1-spk)+srgb(pal['speck'])*spk
    C=body.copy()
    for pid,c in pal['parts'].items():
        sel=P==pid
        if pid in (1,12):  # limbs: same dorsal/belly darkening as the body, plus speck
            cc=srgb(c)*(0.85+0.3*np.maximum(t[sel],0))+ (be-srgb(c))*0.55*np.maximum(-t[sel],0)
            s2=spk[sel]; cc=cc*(1-s2)+srgb(pal['speck'])*s2
            C[sel]=cc
        else: C[sel]=srgb(c)
    return C

def draw(m, R, lo, hi, C, W=520, H=400, label=None):
    V,N,F=m['V'],m['N'],m['F']
    X=V@R.T; Nn=N@R.T
    span=max((hi[0]-lo[0])/W,(hi[1]-lo[1])/H)*1.08
    cx,cy=(lo[0]+hi[0])/2,(lo[1]+hi[1])/2
    px=(X[:,0]-cx)/span+W/2; py=H/2-(X[:,1]-cy)/span; pz=X[:,2]
    img=np.zeros((H,W,3)); img[:]=np.array([0.93,0.94,0.95])
    # ground: shade pixels where a ray hits y=0 (orthographic): solve for each pixel
    Ri=R.T
    gx,gy=np.meshgrid(np.arange(W)+0.5,np.arange(H)+0.5)
    sx=(gx-W/2)*span+cx; sy=(H/2-gy)*span+cy
    # world point = Ri @ (sx, sy, z); y_world = Ri[1,0]*sx + Ri[1,1]*sy + Ri[1,2]*z = 0
    zb=np.full((H,W),-1e9)
    if abs(Ri[1,2])>1e-3:
        z=-(Ri[1,0]*sx+Ri[1,1]*sy)/Ri[1,2]
        wx=Ri[0,0]*sx+Ri[0,1]*sy+Ri[0,2]*z; wz=Ri[2,0]*sx+Ri[2,1]*sy+Ri[2,2]*z
        chk=((np.floor(wx/0.05)+np.floor(wz/0.05))%2)
        g=np.where(chk[...,None]>0,np.array([0.74,0.71,0.65]),np.array([0.80,0.77,0.70]))
        img=g; zb=z.copy()
    L=np.array([0.35,0.8,0.5]); L/=np.linalg.norm(L)
    Lv=L@R.T
    nrm=Nn/np.maximum(np.linalg.norm(Nn,axis=1,keepdims=True),1e-9)
    lam=np.clip(nrm@Lv,0,1); amb=0.32+0.12*np.clip(nrm[:,1]* (1),0,1)
    spec=np.clip(nrm@((Lv+np.array([0,0,1]))/np.linalg.norm(Lv+np.array([0,0,1]))),0,1)**24*0.25
    shade=(amb+0.75*lam)[:,None]*C+spec[:,None]
    for f in F:
        x=px[f]; y=py[f]
        x0=max(int(np.floor(x.min())),0); x1=min(int(np.ceil(x.max())),W-1)
        y0=max(int(np.floor(y.min())),0); y1=min(int(np.ceil(y.max())),H-1)
        if x0>x1 or y0>y1: continue
        d=(x[1]-x[0])*(y[2]-y[0])-(x[2]-x[0])*(y[1]-y[0])
        if abs(d)<1e-12: continue
        xs,ys=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
        w1=((xs-x[0])*(y[2]-y[0])-(x[2]-x[0])*(ys-y[0]))/d
        w2=((x[1]-x[0])*(ys-y[0])-(xs-x[0])*(y[1]-y[0]))/d
        w0=1-w1-w2
        ins=(w0>=-1e-6)&(w1>=-1e-6)&(w2>=-1e-6)
        if not ins.any(): continue
        z=w0*pz[f[0]]+w1*pz[f[1]]+w2*pz[f[2]]
        sub=zb[y0:y1+1,x0:x1+1]
        ok=ins&(z>sub)
        if not ok.any(): continue
        col=w0[...,None]*shade[f[0]]+w1[...,None]*shade[f[1]]+w2[...,None]*shade[f[2]]
        sub[ok]=z[ok]
        im=img[y0:y1+1,x0:x1+1]; im[ok]=col[ok]
    out=Image.fromarray((np.clip(img,0,1)*255).astype(np.uint8))
    if label: ImageDraw.Draw(out).text((6,4),label,fill=(0,0,0))
    return out

def render(objp, outp, pal='king', views=('top','side','front','front34','lowfront34','rear34'), W=520, H=400, cols=3):
    m=load(objp); C=colours(m, PAL[pal])
    ims=[]
    for v in views:
        R=VIEWS[v]; X=m['V']@R.T
        lo=X.min(axis=0); hi=X.max(axis=0)
        ims.append(draw(m,R,lo,hi,C,W,H,label=v))
    rows=(len(ims)+cols-1)//cols
    s=Image.new('RGB',(W*cols,H*rows),(255,255,255))
    for k,im in enumerate(ims): s.paste(im,((k%cols)*W,(k//cols)*H))
    s.save(outp)

if __name__=='__main__':
    a=sys.argv
    views=a[4].split(',') if len(a)>4 else ('top','side','front','front34','lowfront34','rear34')
    render(a[1],a[2],a[3] if len(a)>3 else 'king',views)
